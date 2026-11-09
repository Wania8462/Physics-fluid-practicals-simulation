using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Measures how deep the fluid is under each depth meter, and remembers it. Everything is in sim units</summary>
public class DepthMeters
{
    public const int MaxMeters = 8;
    /// <summary>Sim seconds of history kept</summary>
    public const float MaxHistory = 60;

    private const float SampleInterval = 0.1f;
    /// <summary>Half the width of the column of fluid each meter measures, in particle spacings</summary>
    private const float ColumnRadius = 3;

    private class Meter
    {
        public float x, z;
        public float depth;
        /// <summary>Sim time and depth of each sample, oldest first</summary>
        public readonly List<Vector2> history = new();
    }

    private readonly SimulationManager manager;
    private readonly Meter[] meters = new Meter[MaxMeters];

    private float time;
    private float nextSample;
    private bool readbackPending;
    private bool moved;

    /// <summary>Goes up whenever a reading changes</summary>
    public int Revision { get; private set; }

    private float ColumnHalfWidth => ColumnRadius * manager.Spacing;

    public DepthMeters(SimulationManager manager)
    {
        this.manager = manager;
        manager.StepStarting += dt => time += dt;
        manager.StepFinished += RequestSample;
    }

    /// <summary>Puts a meter in the middle of the tank</summary>
    public void Add(int meter)
    {
        meters[meter] = new Meter { depth = manager.SurfaceHeight + manager.Tank.halfSize.y };
        moved = true;
    }

    public void Remove(int meter) => meters[meter] = null;

    public bool Has(int meter) => meters[meter] != null;

    /// <summary>Moves a meter across the tank floor, keeping its whole column inside</summary>
    public void Move(int meter, float x, float z)
    {
        float3 limit = manager.Tank.halfSize - ColumnHalfWidth;
        meters[meter].x = Mathf.Clamp(x, -limit.x, limit.x);
        meters[meter].z = Mathf.Clamp(z, -limit.z, limit.z);
        moved = true;
    }

    /// <summary>The middle of the meter's ray, halfway up the tank</summary>
    public Vector3 Position(int meter) => new(meters[meter].x, 0, meters[meter].z);

    public float Depth(int meter) => meters[meter].depth;

    /// <summary>Samples from the last seconds, as seconds before the newest one (at most 0) and depth</summary>
    public Vector2[] History(int meter, float seconds)
    {
        List<Vector2> history = meters[meter].history;
        if (history.Count == 0)
            return new Vector2[0];

        float newest = history[^1].x;
        int first = Mathf.Max(0, history.FindIndex(sample => sample.x >= newest - seconds));

        Vector2[] result = new Vector2[history.Count - first];
        for (int i = 0; i < result.Length; i++)
            result[i] = new Vector2(history[first + i].x - newest, history[first + i].y);
        return result;
    }

    /// <summary>Asks for the particles when a sample is due, or to update a meter that moved while paused</summary>
    private void RequestSample()
    {
        if (readbackPending || System.Array.TrueForAll(meters, meter => meter == null))
            return;

        bool due = time >= nextSample;
        if (!due && !moved)
            return;

        if (due)
            nextSample = time + SampleInterval;

        moved = false;
        readbackPending = true;
        float sampleTime = time;
        AsyncGPUReadback.Request(manager.Buffers["Positions"], request => FinishSample(request, due, sampleTime));
    }

    /// <summary>Works out each meter's depth from how much fluid is in its column</summary>
    private void FinishSample(AsyncGPUReadbackRequest request, bool record, float sampleTime)
    {
        // The scene may have closed since the request
        if (!manager) return;

        readbackPending = false;
        if (request.hasError) return;

        var positions = request.GetData<float4>();
        float radius = ColumnHalfWidth;
        float[] sums = new float[MaxMeters];

        // ponytail: loops over every particle on the CPU each sample, a few ms. Move it to a kernel if it shows in the profiler
        for (int i = 0; i < positions.Length; i++)
        {
            float4 position = positions[i];
            for (int m = 0; m < MaxMeters; m++)
            {
                if (meters[m] == null) continue;

                // Particles near the middle of the column count most, so readings change smoothly as they cross it
                float distanceX = math.abs(position.x - meters[m].x);
                float distanceZ = math.abs(position.z - meters[m].z);
                if (distanceX < radius && distanceZ < radius)
                    sums[m] += (1 - distanceX / radius) * (1 - distanceZ / radius);
            }
        }

        float heightPerWeight = math.pow(manager.Spacing, 3) / (radius * radius);
        for (int m = 0; m < MaxMeters; m++)
        {
            if (meters[m] == null) continue;

            meters[m].depth = sums[m] * heightPerWeight;
            if (record)
                AddToHistory(meters[m], sampleTime);
        }

        Revision++;
    }

    private static void AddToHistory(Meter meter, float sampleTime)
    {
        meter.history.Add(new Vector2(sampleTime, meter.depth));

        int kept = meter.history.FindIndex(sample => sample.x >= sampleTime - MaxHistory);
        if (kept > 0)
            meter.history.RemoveRange(0, kept);
    }
}
