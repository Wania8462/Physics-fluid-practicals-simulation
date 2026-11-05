using System;
using Unity.Mathematics;
using UnityEngine;

/// <summary>Builds simple meshes in code</summary>
public static class MeshGenerator
{
    /// <summary>Rectangle centred on the origin</summary>
    public static Mesh Rectangle(float width, float height)
    {
        Vector3[] verticies = new Vector3[]
        {
                new(-0.5f * width, 0.5f * height),
                new(0.5f * width, 0.5f * height),
                new(-0.5f * width, -0.5f * height),
                new(0.5f * width, -0.5f * height)
        };

        int[] triangles = new int[] { 2, 0, 1, 3, 2, 1 };
        Mesh mesh = new()
        {
            vertices = verticies,
            triangles = triangles
        };

        return mesh;
    }

    /// <summary>Flat strip from start to end, null if they're the same point</summary>
    public static Mesh Line(float2 start, float2 end, float width)
    {
        float2 perpendicular = end - start;
        float magnitude = math.length(perpendicular);
        if (magnitude < Mathf.Epsilon) return null;
        perpendicular = new(-perpendicular.y, perpendicular.x);
        perpendicular /= Mathf.Sqrt(perpendicular.x * perpendicular.x + perpendicular.y * perpendicular.y);
        perpendicular *= width * 0.5f;

        Vector3[] verticies = new Vector3[]
        {
                new(start.x + perpendicular.x, start.y + perpendicular.y),
                new(start.x - perpendicular.x, start.y - perpendicular.y),
                new(end.x + perpendicular.x, end.y + perpendicular.y),
                new(end.x - perpendicular.x, end.y - perpendicular.y)
        };

        int[] triangles = new int[] { 1, 0, 2, 1, 2, 3 };
        Mesh mesh = new()
        {
            vertices = verticies,
            triangles = triangles
        };

        return mesh;
    }

    /// <summary>The 12 edges of a box centred on the origin, as lines</summary>
    public static Mesh WireBox(float3 size)
    {
        float3 h = size / 2;

        Vector3[] verticies = new Vector3[]
        {
                new(-h.x, -h.y, -h.z), new(h.x, -h.y, -h.z), new(h.x, -h.y, h.z), new(-h.x, -h.y, h.z),
                new(-h.x, h.y, -h.z), new(h.x, h.y, -h.z), new(h.x, h.y, h.z), new(-h.x, h.y, h.z)
        };

        int[] lines = new int[]
        {
            0, 1, 1, 2, 2, 3, 3, 0,
            4, 5, 5, 6, 6, 7, 7, 4,
            0, 4, 1, 5, 2, 6, 3, 7
        };

        Mesh mesh = new() { vertices = verticies };
        mesh.SetIndices(lines, MeshTopology.Lines, 0);

        return mesh;
    }

    /// <summary>Flat disc in the XY plane, null for a bad radius or resolution</summary>
    public static Mesh Circle(float radius, int resolution)
    {
        if (radius <= 0)
        {
            Debug.LogError($"Mesh generator: Mesh can't have radius of {radius}");
            return null;
        }

        if (resolution <= 0)
        {
            Debug.LogError($"Mesh generator: Mesh can't have resolution of {resolution}");
            return null;
        }

        Vector3[] verticies = new Vector3[4 * resolution + 1];
        int[] triangles = new int[resolution * 12];

        verticies[0] = new(0, 0);

        for (int i = 1; i < verticies.Length; i++)
        {
            verticies[i] = new((float)(radius * Math.Cos(Math.PI * (i - 1) / resolution / 2)),
                               (float)(radius * Math.Sin(Math.PI * (i - 1) / resolution / 2)),
                               0);
        }

        int v = 1;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            triangles[i] = 0;
            triangles[i + 1] = v + 1;
            triangles[i + 2] = v;
            v += 1;
        }

        triangles[^2] = 1;

        Mesh mesh = new()
        {
            vertices = verticies,
            triangles = triangles
        };

        return mesh;
    }

    /// <summary>One mesh per marching squares case</summary>
    public static Mesh[] MarchingSquareVariations()
    {
        const float Up = 0.5f;
        const float RightDir = 0.5f;
        const float Down = -0.5f;
        const float LeftDir = -0.5f;
        const int TL = 0;
        const int TR = 1;
        const int BL = 2;
        const int BR = 3;

        const int Top = 4;
        const int Right = 5;
        const int Bottom = 6;
        const int Left = 7;

        Vector3[] vertices =
        {
            new(LeftDir, Up),
            new(RightDir, Up),
            new(LeftDir, Down),
            new(RightDir, Down),

            new(0, Up),
            new(RightDir, 0),
            new(0, Down),
            new(LeftDir, 0)
        };

        // Indexed by which corners are filled: TL = 1, TR = 2, BR = 4, BL = 8
        int[][] triangles = new int[][]
        {
            new int[] { },
            new int[] { Left, TL, Top },
            new int[] { Top, TR, Right },
            new int[] { Left, TL, TR,  Left, TR, Right },
            new int[] { Right, BR, Bottom },
            new int[] { Left, TL, Top,  Right, BR, Bottom },
            new int[] { Top, TR, BR,  Top, BR, Bottom },
            new int[] { Left, TL, TR,  Left, TR, BR,  Left, BR, Bottom },
            new int[] { Bottom, BL, Left },
            new int[] { Top, Bottom, BL,  Top, BL, TL },
            new int[] { Top, TR, Right,  Bottom, BL, Left },
            new int[] { TL, TR, Right,  TL, Right, Bottom,  TL, Bottom, BL },
            new int[] { Right, BR, BL,  Right, BL, Left },
            new int[] { TL, Top, Right,  TL, Right, BR,  TL, BR, BL },
            new int[] { Top, TR, BR,  Top, BR, BL,  Top, BL, Left },
            new int[] { TL, TR, BR,  TL, BR, BL }
        };

        var meshes = new Mesh[16];

        for (int i = 0; i < triangles.Length; i++)
        {
            meshes[i] = new Mesh()
            {
                vertices = vertices,
                triangles = triangles[i]
            };
        }

        return meshes;
    }
}