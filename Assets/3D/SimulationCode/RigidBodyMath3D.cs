using Unity.Mathematics;

/// <summary>Rigid body shape math for the C# side. RigidBodyMath3D.hlsl has the GPU side</summary>
public static class RigidBodyMath3D
{
    #region Volume and inertia
    public static float SphereVolume(float radius) => 4f / 3 * math.PI * radius * radius * radius;

    /// <summary>The radius of a sphere with this volume</summary>
    public static float SphereRadiusFromVolume(float volume) => math.pow(3 * volume / (4 * math.PI), 1f / 3);

    /// <summary>Half the edge length of a cube with this volume</summary>
    public static float CubeHalfEdgeFromVolume(float volume) => math.pow(volume, 1f / 3) / 2;

    /// <summary>Moment of inertia of a solid sphere about its centre</summary>
    public static float SphereInertia(float mass, float radius) => 0.4f * mass * radius * radius;

    /// <summary>Moment of inertia of a solid cube about its centre</summary>
    public static float CubeInertia(float mass, float halfEdge) => mass * (2 * halfEdge) * (2 * halfEdge) / 6;
    #endregion

    #region Signed distances
    /// <summary>Distance to a box's surface, negative inside</summary>
    public static float BoxSignedDistance(float3 local, float3 halfExtents)
    {
        float3 q = math.abs(local) - halfExtents;
        return math.length(math.max(q, 0f)) + math.min(math.cmax(q), 0f);
    }

    /// <summary>Distance to a cylinder standing along y, negative inside</summary>
    public static float CylinderSignedDistance(float3 local, float radius, float halfHeight)
    {
        float2 d = new(math.length(local.xz) - radius, math.abs(local.y) - halfHeight);
        return math.length(math.max(d, 0f)) + math.min(math.cmax(d), 0f);
    }
    #endregion

    #region Submerged volume
    /// <summary>Fraction of a sphere's volume below a flat plane. A smoothstep, close to the exact cap volume</summary>
    /// <param name="depth">The plane's height above the centre, over the diameter</param>
    public static double SphereFractionBelow(double depth)
    {
        double x = math.saturate(depth + 0.5);
        return x * x * (3 - 2 * x);
    }

    /// <summary>Exact fraction of a cube's volume below a flat plane</summary>
    /// <param name="depth">The plane's height above the centre, over the edge length</param>
    /// <param name="up">The plane's normal in the cube's frame</param>
    public static double CubeFractionBelow(double depth, float3 up)
    {
        // Tiny instead of zero, because the sum below divides by each component
        double3 n = math.max(math.abs((double3)up), 1e-3);
        double level = depth + (n.x + n.y + n.z) / 2;

        // Inclusion-exclusion over the corners of the unit cube
        double volume = 0;
        for (int corner = 0; corner < 8; corner++)
        {
            int a = corner & 1, b = (corner >> 1) & 1, c = (corner >> 2) & 1;
            double past = math.max(level - (a * n.x + b * n.y + c * n.z), 0.0);
            volume += ((a ^ b ^ c) == 0 ? 1 : -1) * past * past * past;
        }

        return math.saturate(volume / (6 * n.x * n.y * n.z));
    }
    #endregion

    #region Rotated boxes
    /// <summary>How far a rotated box reaches from its centre along each world axis</summary>
    /// <param name="basis">The box's rotation, its columns are the box's axes</param>
    public static float3 BoxReach(float3x3 basis, float3 halfExtents)
        => math.abs(basis.c0) * halfExtents.x + math.abs(basis.c1) * halfExtents.y + math.abs(basis.c2) * halfExtents.z;

    /// <summary>Which of the box's axes points most along a world axis</summary>
    public static int MostAlignedAxis(float3x3 basis, int worldAxis)
    {
        int nearest = 0;
        for (int k = 1; k < 3; k++)
            if (math.abs(basis[k][worldAxis]) > math.abs(basis[nearest][worldAxis])) nearest = k;
        return nearest;
    }
    #endregion
}
