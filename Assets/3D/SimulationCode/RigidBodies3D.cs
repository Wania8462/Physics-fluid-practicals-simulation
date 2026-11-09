using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

/// <summary>Matches the shape ids in RigidBodyMath3D.hlsl</summary>
public enum BodyShape3D
{
    Cube = 0,
    Sphere = 1,
    Scale = 2
}

/// <summary>Inspector settings of one rigid body</summary>
[Serializable]
public struct RigidBodySettings3D
{
    public BodyShape3D shape;
    [Tooltip("Sphere radius, or half the edge length of a cube")]
    public float bodyRadius;
    [Tooltip("Spacing between the boundary particles sampled on the surface")]
    public float sampleDensity;
    public float3 bodyPosition;
    [Tooltip("Euler angles in degrees")]
    public float3 bodyRotation;
    public float boundaryBodyMass;
    [Tooltip("Held still, but the fluid's push on it is still measured. Scales are always held")]
    public bool fixedInPlace;
    [Tooltip("Scales only: which scale this is, below RigidBodies3D.MaxScales")]
    public int scale;

    /// <summary>An empty slot in the bodies array</summary>
    public bool IsEmpty => bodyRadius == 0;
}

/// <summary>Mirrors RigidBodyProperties in RigidBodyMath3D.hlsl</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RigidBodyProperties3D
{
    public float4 size;
    public float4 centerOfMass;
    public float4 invInertia;
    public uint shape;
    public uint start;
    public uint count;
    public float invMass;
}

/// <summary>Mirrors RigidBodyState in RigidBodyMath3D.hlsl</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RigidBodyState3D
{
    public float4 position;
    public float4 rotation;
    public float4 prevPosition;
    public float4 prevRotation;
    public float4 velocity;
    public float4 angularVelocity;
}

/// <summary>CPU copy of the rigid body data sent to the GPU</summary>
public class RigidBodyData3D
{
    public List<RigidBodySettings3D> settings = new();
    public List<int> inspectorIndices = new();
    public List<RigidBodyProperties3D> properties = new();
    public List<RigidBodyState3D> states = new();
    public List<float4> boundaryPositions = new();
    public List<float4> boundaryRestPositions = new();
    public List<uint> boundaryBodyIndices = new();

    public int NumBodies => properties.Count;
    public int NumBoundaryParticles => boundaryPositions.Count;
}

/// <summary>
/// Adding a shape: a BodyShape3D value, a sampler in Spawn3DParticles, cases in the switches below,
/// and a signed distance in RigidBodyMath3D.hlsl
/// </summary>
public static class RigidBodies3D
{
    public const int MaxBodies = 10;

    // ponytail: scales come in one fixed size, big enough for a 2 L block
    /// <summary>Most scales at once. The scale constants must match RigidBodyMath3D.hlsl</summary>
    public const int MaxScales = 2;
    public static readonly float3 ScaleHalfExtents = new(25, 4, 25);
    public const float ScalePanRadius = 20;
    public const float ScalePanHalfHeight = 1.5f;

    /// <summary>Threads per body in the summing kernels. These constants must match the hlsl</summary>
    public const int ReductionThreads = 64;

    public const int CouplingSlotSize = 2;
    public const int ContactSlotSize = (4 + MaxScales) * 4;

    public const int ForceCount = 3;
    /// <summary>Matches ForceBuoyancy in RigidBodyMath3D.hlsl</summary>
    public const int ForceBuoyancy = 2;

    /// <summary>Builds every valid body in the inspector array</summary>
    public static RigidBodyData3D Build(RigidBodySettings3D[] inspectorBodies, Spawn3DParticles spawn, float interactionRadius, Tank tank)
    {
        RigidBodyData3D data = new();

        int inspectorCount = inspectorBodies?.Length ?? 0;
        for (int i = 0; i < inspectorCount; i++)
        {
            RigidBodySettings3D body = inspectorBodies[i];

            if (body.IsEmpty)
                continue;

            if (data.NumBodies == MaxBodies)
            {
                Debug.LogError($"Rigid bodies: at most {MaxBodies} bodies are supported, ignoring body {i} and the ones after it");
                break;
            }

            if (body.bodyRadius <= 0 || body.sampleDensity <= 0)
            {
                Debug.LogError($"Rigid bodies: body {i} needs bodyRadius and sampleDensity above 0 (got {body.bodyRadius} and {body.sampleDensity}), ignoring it");
                continue;
            }

            if (body.sampleDensity >= interactionRadius)
                Debug.LogWarning($"Rigid bodies: body {i} has sampleDensity {body.sampleDensity} >= interactionRadius {interactionRadius}, its boundary particles won't see each other and their volumes will be infinite");

            AddBody(data, body, i, spawn);
        }

        WarnAboutWalls(data, tank.innerHalfSize);
        WarnAboutOverlaps(data);
        return data;
    }

    /// <summary>Adds a body and samples its surface into boundary particles</summary>
    public static void AddBody(RigidBodyData3D data, RigidBodySettings3D body, int inspectorIndex, Spawn3DParticles spawn)
    {
        uint bodyIndex = (uint)data.NumBodies;
        float3[] samples = SampleSurface(body, spawn);
        quaternion rotation = GetRotation(body);

        RigidBodyProperties3D bodyProperties = new()
        {
            size = body.shape == BodyShape3D.Scale ? new float4(ScaleHalfExtents, body.scale) : new float4(new float3(body.bodyRadius), 0),
            centerOfMass = float4.zero,
            shape = (uint)body.shape,
            start = (uint)data.boundaryPositions.Count,
            count = (uint)samples.Length
        };
        SetMassProperties(ref bodyProperties, body);

        float4 centerOfMass = new(body.bodyPosition + math.mul(rotation, bodyProperties.centerOfMass.xyz), 0);
        data.states.Add(new RigidBodyState3D
        {
            position = centerOfMass,
            rotation = rotation.value,
            prevPosition = centerOfMass,
            prevRotation = rotation.value
        });

        foreach (float3 sample in samples)
        {
            float3 rest = sample - bodyProperties.centerOfMass.xyz;
            data.boundaryRestPositions.Add(new float4(rest, 0));
            data.boundaryPositions.Add(new float4(centerOfMass.xyz + math.mul(rotation, rest), 0));
            data.boundaryBodyIndices.Add(bodyIndex);
        }

        data.settings.Add(body);
        data.inspectorIndices.Add(inspectorIndex);
        data.properties.Add(bodyProperties);
    }

    /// <summary>Removes a body and its boundary particles. The other bodies keep their state</summary>
    public static void RemoveBody(RigidBodyData3D data, int slot)
    {
        // Build skips invalid slots, so there may be nothing to remove
        int index = data.inspectorIndices.IndexOf(slot);
        if (index < 0) return;

        RigidBodyProperties3D removed = data.properties[index];
        int start = (int)removed.start;
        int count = (int)removed.count;
        data.boundaryPositions.RemoveRange(start, count);
        data.boundaryRestPositions.RemoveRange(start, count);
        data.boundaryBodyIndices.RemoveRange(start, count);

        for (int i = start; i < data.NumBoundaryParticles; i++)
            data.boundaryBodyIndices[i]--;

        data.settings.RemoveAt(index);
        data.inspectorIndices.RemoveAt(index);
        data.properties.RemoveAt(index);
        data.states.RemoveAt(index);

        for (int b = index; b < data.NumBodies; b++)
        {
            RigidBodyProperties3D properties = data.properties[b];
            properties.start -= removed.count;
            data.properties[b] = properties;
        }
    }

    /// <summary>Sets a body's mass and how hard it is to spin. Scales and fixed bodies can't move</summary>
    public static void SetMassProperties(ref RigidBodyProperties3D properties, RigidBodySettings3D body)
    {
        if (body.shape == BodyShape3D.Scale || body.fixedInPlace)
        {
            properties.invMass = 0;
            properties.invInertia = float4.zero;
            return;
        }

        float mass = body.boundaryBodyMass > 0 ? body.boundaryBodyMass : properties.count;
        float r = body.bodyRadius;
        float inertia = body.shape switch
        {
            BodyShape3D.Cube => mass * (2 * r) * (2 * r) / 6,
            BodyShape3D.Sphere => 0.4f * mass * r * r,
            _ => throw new ArgumentOutOfRangeException(nameof(body), $"Rigid bodies: no inertia for shape {body.shape}")
        };

        properties.invMass = 1 / mass;
        properties.invInertia = new float4(new float3(1 / inertia), 0);
    }

    /// <summary>The bodyRadius that gives a body this volume</summary>
    public static float RadiusFromVolume(BodyShape3D shape, float volume) => shape switch
    {
        BodyShape3D.Cube => math.pow(volume, 1f / 3) / 2,
        BodyShape3D.Sphere => math.pow(3 * volume / (4 * math.PI), 1f / 3),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), $"Rigid bodies: no volume for shape {shape}")
    };

    // ponytail: assumes one flat waterline per body, so a wave along its side averages out
    /// <summary>Finds the waterline from which surface particles are wet, then the volume below it</summary>
    /// <param name="body">Index into data</param>
    /// <param name="up">World up in the body's frame</param>
    /// <param name="boundaryWet">For every boundary particle, non-zero if it touches fluid</param>
    /// <returns>How much of the body is under the fluid, from 0 to 1</returns>
    public static float SubmergedFraction(RigidBodyData3D data, int body, float3 up, uint[] boundaryWet)
    {
        RigidBodyProperties3D properties = data.properties[body];
        int start = (int)properties.start;
        int count = (int)properties.count;

        float[] heights = new float[count];
        uint[] wet = new uint[count];
        int wrong = 0;
        for (int i = 0; i < count; i++)
        {
            heights[i] = math.dot(data.boundaryRestPositions[start + i].xyz, up);
            wet[i] = boundaryWet[start + i];
            if (wet[i] != 0) wrong++;
        }
        Array.Sort(heights, wet);

        // The waterline most particles agree with: wet below it and dry above it, so a puddle on top gets outvoted
        int fewestWrong = wrong;
        int below = 0;
        for (int i = 0; i < count; i++)
        {
            wrong += wet[i] != 0 ? -1 : 1;
            if (wrong < fewestWrong)
            {
                fewestWrong = wrong;
                below = i + 1;
            }
        }

        if (below == 0) return 0;
        if (below == count) return 1;
        return (float)VolumeFractionBelow(data.settings[body], up, (heights[below - 1] + heights[below]) / 2);
    }

    /// <summary>Fraction of a body's volume below a flat plane</summary>
    /// <param name="height">The plane's height above the body's centre</param>
    private static double VolumeFractionBelow(RigidBodySettings3D body, float3 up, float height)
    {
        double depth = height / (2.0 * body.bodyRadius);

        switch (body.shape)
        {
            case BodyShape3D.Sphere:
                double x = math.saturate(depth + 0.5);
                return x * x * (3 - 2 * x);

            case BodyShape3D.Cube:
                // Tiny instead of zero, because the sum below divides by each component
                double3 n = math.max(math.abs((double3)up), 1e-3);
                double level = depth + (n.x + n.y + n.z) / 2;

                double volume = 0;
                for (int corner = 0; corner < 8; corner++)
                {
                    int a = corner & 1, b = (corner >> 1) & 1, c = (corner >> 2) & 1;
                    double past = math.max(level - (a * n.x + b * n.y + c * n.z), 0.0);
                    volume += ((a ^ b ^ c) == 0 ? 1 : -1) * past * past * past;
                }

                return math.saturate(volume / (6 * n.x * n.y * n.z));

            default:
                throw new ArgumentOutOfRangeException(nameof(body), $"Rigid bodies: no submerged volume for shape {body.shape}");
        }
    }

    /// <summary>Applies mass changes live, other settings need a reset</summary>
    public static void UpdateMasses(RigidBodyData3D data, RigidBodySettings3D[] inspectorBodies, ComputeBuffer propertiesBuffer)
    {
        if (data == null || data.NumBodies == 0 || inspectorBodies == null) return;

        for (int b = 0; b < data.NumBodies; b++)
        {
            int index = data.inspectorIndices[b];

            // Skip bodies removed from the inspector array, and scales
            if (index >= inspectorBodies.Length || inspectorBodies[index].shape == BodyShape3D.Scale) continue;

            float mass = inspectorBodies[index].boundaryBodyMass;
            if (mass <= 0)
                Debug.LogWarning($"Rigid bodies: body {index} has boundaryBodyMass {mass}, falling back to its boundary particle count ({data.properties[b].count})");

            RigidBodySettings3D body = data.settings[b];
            body.boundaryBodyMass = mass;
            data.settings[b] = body;

            RigidBodyProperties3D properties = data.properties[b];
            SetMassProperties(ref properties, body);
            data.properties[b] = properties;
        }

        propertiesBuffer.SetData(data.properties);
    }

    /// <summary>Creates the GPU buffers, at least one element each so they exist without bodies</summary>
    public static void CreateBuffers(Dictionary<string, ComputeBuffer> buffers, RigidBodyData3D data, int numCells)
    {
        int boundaryCount = math.max(data.NumBoundaryParticles, 1);
        int slotCount = math.max(data.NumBodies, 1) * ReductionThreads;

        buffers["BoundaryPositions"] = CreateNonEmptyBuffer(data.boundaryPositions);
        buffers["BoundaryVelocities"] = ComputeHelper.CreateStructuredBufferWithData<float4>(boundaryCount);
        buffers["BoundaryRestPositions"] = CreateNonEmptyBuffer(data.boundaryRestPositions);
        buffers["BoundaryVolumes"] = ComputeHelper.CreateStructuredBufferWithData<float>(boundaryCount);
        buffers["BoundaryForceBuffersX"] = ComputeHelper.CreateStructuredBufferWithData<int>(boundaryCount);
        buffers["BoundaryForceBuffersY"] = ComputeHelper.CreateStructuredBufferWithData<int>(boundaryCount);
        buffers["BoundaryForceBuffersZ"] = ComputeHelper.CreateStructuredBufferWithData<int>(boundaryCount);
        buffers["BoundaryBodyIndices"] = CreateNonEmptyBuffer(data.boundaryBodyIndices);
        buffers["BoundaryCellStart"] = ComputeHelper.CreateStructuredBufferWithData<uint>(numCells + 1);
        buffers["BoundarySortedIndices"] = ComputeHelper.CreateStructuredBufferWithData<uint>(boundaryCount);

        buffers["BodyProperties"] = CreateNonEmptyBuffer(data.properties);
        buffers["BodyStates"] = CreateNonEmptyBuffer(data.states);
        buffers["BodyCouplingSums"] = ComputeHelper.CreateStructuredBufferWithData<float4>(slotCount * CouplingSlotSize);
        buffers["BodyContactSums"] = ComputeHelper.CreateStructuredBufferWithData<float4>(slotCount * ContactSlotSize);
        buffers["BodyForces"] = ComputeHelper.CreateStructuredBufferWithData<float4>(math.max(data.NumBodies, 1) * ForceCount);
        buffers["SmoothedBodyForces"] = ComputeHelper.CreateStructuredBufferWithData<float4>(math.max(data.NumBodies, 1) * ForceCount);
        buffers["BodyScaleForces"] = ComputeHelper.CreateStructuredBufferWithData<float4>(math.max(data.NumBodies, 1) * MaxScales);
        buffers["BodySubmergedUp"] = ComputeHelper.CreateStructuredBufferWithData<float4>(math.max(data.NumBodies, 1));
        buffers["BoundaryWet"] = ComputeHelper.CreateStructuredBufferWithData<uint>(boundaryCount);

        buffers["BoundaryPositionsRO"] = buffers["BoundaryPositions"];
        buffers["BoundaryVelocitiesRO"] = buffers["BoundaryVelocities"];
        buffers["BoundaryVolumesRO"] = buffers["BoundaryVolumes"];
        buffers["BoundaryCellStartRO"] = buffers["BoundaryCellStart"];
        buffers["BoundarySortedIndicesRO"] = buffers["BoundarySortedIndices"];
    }

    /// <summary>Removes fluid particles closer than margin to a body</summary>
    public static float4[] RemoveParticlesInsideBodies(float4[] positions, List<RigidBodySettings3D> bodies, float margin)
    {
        if (bodies.Count == 0) return positions;

        quaternion[] inverseRotations = new quaternion[bodies.Count];
        for (int b = 0; b < bodies.Count; b++)
            inverseRotations[b] = math.inverse(GetRotation(bodies[b]));

        List<float4> kept = new(positions.Length);
        foreach (float4 position in positions)
        {
            bool inside = false;
            for (int b = 0; b < bodies.Count && !inside; b++)
            {
                float3 local = math.mul(inverseRotations[b], position.xyz - bodies[b].bodyPosition);
                inside = SignedDistance(bodies[b], local) < margin;
            }

            if (!inside)
                kept.Add(position);
        }

        return kept.ToArray();
    }

    /// <summary>Warns about bodies that start partly outside the tank</summary>
    private static void WarnAboutWalls(RigidBodyData3D data, float3 innerHalfSize)
    {
        for (int b = 0; b < data.NumBodies; b++)
        {
            RigidBodyProperties3D body = data.properties[b];
            float3 extent = float3.zero;

            for (int i = (int)body.start; i < body.start + body.count; i++)
                extent = math.max(extent, math.abs(data.boundaryPositions[i].xyz));

            if (math.any(extent > innerHalfSize))
                Debug.LogWarning($"Rigid bodies: body {data.inspectorIndices[b]} starts partly outside the tank and will be pushed back in. Its surface has to stay within x ±{innerHalfSize.x}, y ±{innerHalfSize.y}, z ±{innerHalfSize.z}");
        }
    }

    /// <summary>Warns about bodies that start inside each other</summary>
    private static void WarnAboutOverlaps(RigidBodyData3D data)
    {
        for (int a = 0; a < data.NumBodies; a++)
        {
            for (int b = a + 1; b < data.NumBodies; b++)
            {
                if (StartsInside(data, a, b) || StartsInside(data, b, a))
                    Debug.LogWarning($"Rigid bodies: bodies {data.inspectorIndices[a]} and {data.inspectorIndices[b]} start inside each other, move them apart");
            }
        }
    }

    /// <summary>Whether any boundary particle of body a starts inside body b</summary>
    private static bool StartsInside(RigidBodyData3D data, int a, int b)
    {
        RigidBodySettings3D other = data.settings[b];
        quaternion inverseRotation = math.inverse(GetRotation(other));
        RigidBodyProperties3D body = data.properties[a];

        for (int i = (int)body.start; i < body.start + body.count; i++)
        {
            float3 local = math.mul(inverseRotation, data.boundaryPositions[i].xyz - other.bodyPosition);
            if (SignedDistance(other, local) < 0)
                return true;
        }

        return false;
    }

    /// <summary>Points on the surface of a body, centred on the origin</summary>
    private static float3[] SampleSurface(RigidBodySettings3D body, Spawn3DParticles spawn) => body.shape switch
    {
        BodyShape3D.Cube => spawn.InitCubeSurfacePositions(body.bodyRadius, body.sampleDensity),
        BodyShape3D.Sphere => spawn.InitSphereSurfacePositions(body.bodyRadius, body.sampleDensity),
        BodyShape3D.Scale => spawn.InitScaleSurfacePositions(ScaleHalfExtents, ScalePanRadius, ScalePanHalfHeight, body.sampleDensity),
        _ => throw new ArgumentOutOfRangeException(nameof(body), $"Rigid bodies: no sampler for shape {body.shape}")
    };

    /// <summary>Distance from a point to a body's surface, negative inside</summary>
    private static float SignedDistance(RigidBodySettings3D body, float3 local) => body.shape switch
    {
        BodyShape3D.Cube => BoxSignedDistance(local, new float3(body.bodyRadius)),
        BodyShape3D.Sphere => math.length(local) - body.bodyRadius,
        BodyShape3D.Scale => math.min(BoxSignedDistance(local, ScaleHalfExtents),
            CylinderSignedDistance(local - new float3(0, ScaleHalfExtents.y + ScalePanHalfHeight, 0), ScalePanRadius, ScalePanHalfHeight)),
        _ => throw new ArgumentOutOfRangeException(nameof(body), $"Rigid bodies: no signed distance for shape {body.shape}")
    };

    /// <summary>Distance to a box's surface, negative inside</summary>
    private static float BoxSignedDistance(float3 local, float3 halfExtents)
    {
        float3 q = math.abs(local) - halfExtents;
        return math.length(math.max(q, 0f)) + math.min(math.cmax(q), 0f);
    }

    /// <summary>Distance to a cylinder standing along y, negative inside</summary>
    private static float CylinderSignedDistance(float3 local, float radius, float halfHeight)
    {
        float2 d = new(math.length(local.xz) - radius, math.abs(local.y) - halfHeight);
        return math.length(math.max(d, 0f)) + math.min(math.cmax(d), 0f);
    }

    private static quaternion GetRotation(RigidBodySettings3D body) => quaternion.Euler(math.radians(body.bodyRotation));

    /// <summary>A buffer of the data, or of one empty element if there's none</summary>
    private static ComputeBuffer CreateNonEmptyBuffer<T>(List<T> data) where T : struct =>
        data.Count > 0
            ? ComputeHelper.CreateStructuredBufferWithData(data.ToArray())
            : ComputeHelper.CreateStructuredBufferWithData<T>(1);
}
