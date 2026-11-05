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

    public int NumCells => columns * rows * layers;

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
    }
}
