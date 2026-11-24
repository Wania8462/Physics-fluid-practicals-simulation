using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

/// <summary>Times frames at a range of particle counts and writes them to a CSV, to see how the cost scales</summary>
public class ParticleCountBenchmark : MonoBehaviour
{
    [SerializeField] private SimulationManager manager;
    [SerializeField] private Spawn3DParticles spawn;
    [Tooltip("Particle counts to run, in order")]
    [SerializeField] private int[] particleCounts = { 10_000, 25_000, 50_000, 100_000, 250_000, 500_000, 1_000_000, 2_000_000, 4_000_000 };
    [Tooltip("Frames to skip after spawning, so setup and the first splash don't count")]
    [SerializeField] private int warmupFrames = 300;
    [Tooltip("Frames timed at each count")]
    [SerializeField] private int recordFrames = 500;

    private IEnumerator Start()
    {
        // The single-count benchmark would log in the middle of the run
        if (TryGetComponent(out PerformanceBenchmark single))
            single.enabled = false;

        // Frames run as fast as they can, so frame time is the real cost
        QualitySettings.vSyncCount = 0;
        manager.paused = false;
        // Only the fluid is measured, and the bodies would sit outside the smaller tanks
        manager.settings.bodies = Array.Empty<RigidBodySettings3D>();

        string path = ResultsPath();
        File.WriteAllText(path, Header());
        Debug.Log($"Particle count benchmark: writing to {path}");

        Transform cam = Camera.main.transform;
        List<float> frameTimes = new();

        foreach (int count in particleCounts)
        {
            spawn.SetParticleCount(count);
            manager.Setup();
            Vector3 cameraPosition = FrameTank(manager.Tank);
            Quaternion cameraRotation = cam.rotation;
            frameTimes.Clear();

            for (int frame = 0; frame < warmupFrames + recordFrames; frame++)
            {
                // Setup and inspector changes put the frame cap back, and the mouse would move the camera
                Application.targetFrameRate = -1;
                cam.SetPositionAndRotation(cameraPosition, cameraRotation);
                yield return null;

                if (frame >= warmupFrames)
                    frameTimes.Add(Time.unscaledDeltaTime * 1000);
            }

            // Written per count, so a crash at a big count keeps the earlier results
            File.AppendAllText(path, Row(frameTimes));
            Debug.Log($"Particle count benchmark: {manager.numParticles} particles done");
        }

        Debug.Log($"Particle count benchmark: finished, results in {path}");
    }

    /// <summary>Sets the camera's far plane for the tank and returns where it sees the whole tank, looking the way it does now</summary>
    private static Vector3 FrameTank(Tank tank)
    {
        Camera cam = Camera.main;
        float radius = math.length(tank.halfSize);
        float fov = Mathf.Min(cam.fieldOfView, Camera.VerticalToHorizontalFieldOfView(cam.fieldOfView, cam.aspect));
        float distance = radius / Mathf.Sin(fov / 2 * Mathf.Deg2Rad);

        cam.farClipPlane = distance + radius;
        // The tank is centred on the origin
        return -cam.transform.forward * distance;
    }

    /// <summary>A new CSV in the project folder in the editor, or next to the exe in a build</summary>
    private static string ResultsPath()
    {
        string gpu = string.Concat(SystemInfo.graphicsDeviceName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(Path.GetDirectoryName(Application.dataPath), $"ParticleCountBenchmark {gpu} {DateTime.Now:yyyy-MM-dd HH-mm-ss}.csv");
    }

    private static string Header() =>
        "GPU,Particle count,Mean ms,Median ms,95th percentile ms,99th percentile ms,Max ms,Mean FPS\n";

    /// <summary>One CSV line: the particle count, then frame time statistics</summary>
    private string Row(List<float> frameTimes)
    {
        float[] sorted = frameTimes.OrderBy(time => time).ToArray();
        float mean = sorted.Average();
        float Percentile(float p) => sorted[Mathf.Min((int)(p * sorted.Length), sorted.Length - 1)];

        float[] values = { mean, Percentile(0.5f), Percentile(0.95f), Percentile(0.99f), sorted[^1], 1000 / mean };

        // Invariant culture, so decimals are always dots and don't clash with the commas
        return $"{SystemInfo.graphicsDeviceName},{ShortCount(manager.numParticles)}," +
               string.Join(",", values.Select(value => value.ToString("0.###", CultureInfo.InvariantCulture))) + "\n";
    }

    /// <summary>A count like 25k or 2M</summary>
    private static string ShortCount(int count) => count >= 1_000_000
        ? (count / 1e6f).ToString("0.##", CultureInfo.InvariantCulture) + "M"
        : count >= 1_000
            ? (count / 1e3f).ToString("0.##", CultureInfo.InvariantCulture) + "k"
            : count.ToString();
}
