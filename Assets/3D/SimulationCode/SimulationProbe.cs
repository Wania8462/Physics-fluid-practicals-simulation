using System;
using UnityEngine;

/// <summary>Shows how far the fluid is from ideal water, so settings can be checked against numbers instead of by eye</summary>
public class SimulationProbe : MonoBehaviour
{
    [SerializeField] private SimulationManager manager;
    [Tooltip("Seconds between readings. Each one stalls the GPU for a moment")]
    [SerializeField] private float interval = 1;

    private float[] densities;
    private Vector4[] velocities;
    private float nextReading;

    private void Update()
    {
        int n = manager.numParticles;
        if (Time.unscaledTime < nextReading || n == 0) return;
        nextReading = Time.unscaledTime + interval;

        if (densities?.Length != n)
        {
            densities = new float[n];
            velocities = new Vector4[n];
        }

        // ponytail: synchronous readback stalls a frame each interval, switch to AsyncGPUReadback if it shows in the profiler
        manager.Buffers["Densities"].GetData(densities);
        manager.Buffers["Velocities"].GetData(velocities);

        // Compression is the PBF constraint itself, so free-surface particles (below rest density) count as 0
        double compressionSum = 0, speedSqSum = 0;
        float maxCompression = 0;
        for (int i = 0; i < n; i++)
        {
            float compression = FluidMath3D.Compression(densities[i]);
            compressionSum += compression;
            maxCompression = Mathf.Max(maxCompression, compression);
            speedSqSum += ((Vector3)velocities[i]).sqrMagnitude;
        }

        Debug.Log($"Probe: compression mean {100 * compressionSum / n:F2}%, max {100 * maxCompression:F1}%, " +
                  $"RMS speed {Math.Sqrt(speedSqSum / n):F2} sim units/s");
    }
}
