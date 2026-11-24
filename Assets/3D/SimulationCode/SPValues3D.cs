using Unity.Mathematics;
using UnityEngine;

/// <summary>Grid of cubic cells over the tank, for neighbour search</summary>
public class SPValues3D
{
    /// <summary>Corner of the grid</summary>
    public float3 offset;
    /// <summary>Edge length of a cell</summary>
    public float length;
    public int columns;
    public int rows;
    public int layers;

    /// <summary>Most cells along one axis, since Morton codes use 10 bits per axis</summary>
    public const int MaxCellsPerAxis = 1024;

    /// <summary>Cells are numbered along a Z-order curve, so this counts every key up to the far corner, including unused ones</summary>
    public int NumCells => (int)Morton((uint)columns - 1, (uint)rows - 1, (uint)layers - 1) + 1;

    /// <summary>Z-order key of a cell. Matches GetCellIndex in Simulation.compute</summary>
    public static uint Morton(uint x, uint y, uint z) => SpreadBits(x) | SpreadBits(y) << 1 | SpreadBits(z) << 2;

    /// <summary>Puts two zero bits between each of the lowest 10 bits</summary>
    private static uint SpreadBits(uint v)
    {
        v &= 0x3ff;
        v = (v | v << 16) & 0x30000ff;
        v = (v | v << 8) & 0x300f00f;
        v = (v | v << 4) & 0x30c30c3;
        v = (v | v << 2) & 0x9249249;
        return v;
    }

    public SPValues3D(float3 bottomLeft, float3 topRight, float length)
    {
        if (length <= 0)
            Debug.LogError($"SPValues3D: length must be > 0, got {length}");

        this.length = length;
        offset = bottomLeft;
        var width = topRight.x - bottomLeft.x;
        var height = topRight.y - bottomLeft.y;
        var depth = topRight.z - bottomLeft.z;

        if (width <= 0 || height <= 0 || depth <= 0)
            Debug.LogWarning($"SPValues3D: grid dimensions are non-positive (width={width}, height={height}, depth={depth}), likely caused by a degenerate bounding box");

        columns = (int)(width / length);
        rows = (int)(height / length);
        layers = (int)(depth / length);

        if (width % length != 0) columns++;
        if (height % length != 0) rows++;
        if (depth % length != 0) layers++;

        if (columns == 0 || rows == 0 || layers == 0)
            Debug.LogWarning($"SPValues3D: grid has zero cells (columns={columns}, rows={rows}, layers={layers}), neighbour queries will return nothing");

        if (columns > MaxCellsPerAxis || rows > MaxCellsPerAxis || layers > MaxCellsPerAxis)
            Debug.LogError($"SPValues3D: grid is {columns}x{rows}x{layers} cells, more than {MaxCellsPerAxis} along an axis, so cell keys overlap. Raise interactionRadius or shrink the tank");
    }
}
