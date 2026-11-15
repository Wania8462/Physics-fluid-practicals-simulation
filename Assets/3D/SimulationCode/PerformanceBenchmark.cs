using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Waits for the fluid to settle, then times frames for a while and logs the result, to compare optimisations</summary>
public class PerformanceBenchmark : MonoBehaviour
{
    [SerializeField] private SimulationManager manager;
    [Tooltip("Seconds to wait before recording, so setup and the first splash don't count")]
    [SerializeField] private float warmupSeconds = 10;
    [Tooltip("Seconds of frames to record")]
    [SerializeField] private float recordSeconds = 30;

    private readonly List<float> frameTimes = new();
    private float startTime;
    private bool done;

    private void Start()
    {
        // Frames run as fast as they can, so frame time is the real cost
        QualitySettings.vSyncCount = 0;
        startTime = Time.unscaledTime;
        Debug.Log($"Benchmark: warming up for {warmupSeconds} s, then recording for {recordSeconds} s");
    }

    private void Update()
    {
        if (done) return;

        // SimulationManager caps the frame rate on setup and on inspector changes
        Application.targetFrameRate = -1;

        float elapsed = Time.unscaledTime - startTime;
        if (elapsed < warmupSeconds || manager.paused) return;

        frameTimes.Add(Time.unscaledDeltaTime * 1000);
        if (elapsed < warmupSeconds + recordSeconds) return;

        done = true;
        Debug.Log(Report());
    }

    /// <summary>Frame time statistics of the recorded frames</summary>
    private string Report()
    {
        float[] sorted = frameTimes.OrderBy(time => time).ToArray();
        float mean = sorted.Average();
        float Percentile(float p) => sorted[Mathf.Min((int)(p * sorted.Length), sorted.Length - 1)];

        return $"Benchmark: {manager.numParticles} particles, {sorted.Length} frames over {recordSeconds} s on {SystemInfo.graphicsDeviceName}\n" +
               $"Frame time ms: mean {mean:F2}, median {Percentile(0.5f):F2}, 95th {Percentile(0.95f):F2}, 99th {Percentile(0.99f):F2}, max {sorted[^1]:F2}\n" +
               $"FPS: mean {1000 / mean:F1}, 99th percentile {1000 / Percentile(0.99f):F1}";
    }
}
