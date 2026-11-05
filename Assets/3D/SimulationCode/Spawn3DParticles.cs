using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>The box the fluid is simulated in, centred on the origin</summary>
public readonly struct Tank
{
    /// <summary>Wall to wall</summary>
    public readonly float3 size;
    /// <summary>Origin to wall</summary>
    public readonly float3 halfSize;
    /// <summary>Origin to where a particle centre touches the wall</summary>
    public readonly float3 innerHalfSize;

    public Tank(float3 size, float particleRadius)
    {
        this.size = size;
        halfSize = size / 2;
        innerHalfSize = halfSize - particleRadius;
    }
}

/// <summary>Spawns the fluid particles and samples the rigid body surfaces</summary>
public class Spawn3DParticles : MonoBehaviour
{
    [Header("Spawn settings")]
    [Tooltip("Centre of the particle box")]
    [SerializeField] private float3 center;
    [Tooltip("Size of the box filled with particles, one per spacing³ cell")]
    [SerializeField] private float3 particleBoxSize = new(40, 40, 40);
    [SerializeField] private float spacing = 2;
    [SerializeField] private bool useJitter = true;
    [SerializeField] private float jitterStrength = 0.2f;
    [Tooltip("Space between the particle box and each tank wall")]
    [SerializeField, FormerlySerializedAs("boundingBoxSizeOffset")] private float3 tankPadding = new(40, 40, 40);

    /// <summary>Distance between spawned particles, which the fluid rests at</summary>
    public float Spacing => spacing;

    private int3 ParticlesPerAxis => math.max(1, (int3)math.floor(particleBoxSize / spacing));

    public int GetNumberOfParticles() => ParticlesPerAxis.x * ParticlesPerAxis.y * ParticlesPerAxis.z;

    /// <summary>A grid of fluid particles filling the spawn box, slightly jittered</summary>
    public float4[] InitializePositions()
    {
        // Warn if the spawn box sticks out of the tank
        if (math.any(math.abs(center) > tankPadding))
            Debug.LogWarning($"Spawn particles: the particle box at {center} sticks out of the tank");

        int3 n = ParticlesPerAxis;
        float4[] pos = new float4[n.x * n.y * n.z];
        float jitter = useJitter ? jitterStrength : 0;
        float3 start = center - (float3)(n - 1) * spacing / 2;

        for (int i = 0; i < n.x; i++)
        {
            for (int j = 0; j < n.y; j++)
            {
                for (int k = 0; k < n.z; k++)
                {
                    float3 jitterOffset = UnityEngine.Random.insideUnitSphere * jitter;
                    pos[(i * n.y + j) * n.z + k] = new float4(start + new float3(i, j, k) * spacing + jitterOffset, 0);
                }
            }
        }

        return pos;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(center, particleBoxSize);
        Gizmos.color = Color.white;
        Gizmos.DrawWireCube(Vector3.zero, TankSize);
    }

    private float3 TankSize => particleBoxSize + tankPadding * 2;

    /// <summary>Evenly spaced points on a cube's surface</summary>
    public float3[] InitCubeSurfacePositions(float halfSize, float sampleDensity)
    {
        int cellsPerEdge = Mathf.Max(1, Mathf.CeilToInt(2 * halfSize / sampleDensity));
        float latticeSpacing = 2 * halfSize / cellsPerEdge;
        List<float3> positions = new();

        for (int i = 0; i <= cellsPerEdge; i++)
        {
            for (int j = 0; j <= cellsPerEdge; j++)
            {
                for (int k = 0; k <= cellsPerEdge; k++)
                {
                    bool onFace = i == 0 || j == 0 || k == 0 || i == cellsPerEdge || j == cellsPerEdge || k == cellsPerEdge;
                    if (onFace)
                        positions.Add(new float3(i, j, k) * latticeSpacing - halfSize);
                }
            }
        }

        return positions.ToArray();
    }

    /// <summary>Evenly spread points on a sphere (Fibonacci spiral)</summary>
    public float3[] InitSphereSurfacePositions(float radius, float sampleDensity)
    {
        int count = Mathf.CeilToInt(4 * math.PI * radius * radius / (sampleDensity * sampleDensity));

        if (count < 4)
        {
            Debug.LogWarning("Spawn particles: not enough particles for the sphere surface");
            count = 4;
        }

        float goldenAngle = math.PI * (3 - math.sqrt(5));
        float3[] positions = new float3[count];

        for (int i = 0; i < count; i++)
        {
            float y = 1 - 2 * (i + 0.5f) / count;
            float ringRadius = math.sqrt(1 - y * y);
            float angle = goldenAngle * i;
            positions[i] = radius * new float3(math.cos(angle) * ringRadius, y, math.sin(angle) * ringRadius);
        }

        return positions;
    }

    /// <summary>Points on a scale's surface, leaving out the parts hidden inside it</summary>
    public float3[] InitScaleSurfacePositions(float3 halfExtents, float panRadius, float panHalfHeight, float sampleDensity)
    {
        int3 cells = math.max(1, (int3)math.ceil(2 * halfExtents / sampleDensity));
        float3 latticeSpacing = 2 * halfExtents / cells;
        List<float3> positions = new();

        for (int i = 0; i <= cells.x; i++)
        {
            for (int j = 0; j <= cells.y; j++)
            {
                for (int k = 0; k <= cells.z; k++)
                {
                    bool onFace = i == 0 || j == 0 || k == 0 || i == cells.x || j == cells.y || k == cells.z;
                    float3 position = new float3(i, j, k) * latticeSpacing - halfExtents;
                    bool underPan = j == cells.y && math.length(position.xz) < panRadius;

                    if (onFace && !underPan)
                        positions.Add(position);
                }
            }
        }

        // Rings around the pan's side, then rings across its top
        int sideLevels = Mathf.Max(1, Mathf.CeilToInt(2 * panHalfHeight / sampleDensity));
        for (int level = 0; level <= sideLevels; level++)
            AddRing(positions, panRadius, halfExtents.y + 2 * panHalfHeight * level / sideLevels, sampleDensity);

        int topRings = Mathf.Max(1, Mathf.CeilToInt(panRadius / sampleDensity));
        for (int ring = 0; ring < topRings; ring++)
            AddRing(positions, panRadius * ring / topRings, halfExtents.y + 2 * panHalfHeight, sampleDensity);

        return positions.ToArray();
    }

    /// <summary>Evenly spaced points around a horizontal circle</summary>
    private static void AddRing(List<float3> positions, float radius, float y, float sampleDensity)
    {
        int count = Mathf.Max(1, Mathf.CeilToInt(2 * math.PI * radius / sampleDensity));
        for (int i = 0; i < count; i++)
        {
            float angle = 2 * math.PI * i / count;
            positions.Add(new float3(radius * math.cos(angle), y, radius * math.sin(angle)));
        }
    }

    /// <summary>The tank around the spawn box</summary>
    public Tank GetTank(float particleRadius)
    {
        float3 size = TankSize;

        if (math.any(size <= 0))
            Debug.LogWarning($"Spawn particles: tank size is {size}");

        return new Tank(size, particleRadius);
    }
}
