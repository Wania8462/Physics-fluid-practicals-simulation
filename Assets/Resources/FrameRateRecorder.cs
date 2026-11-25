#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Editor menu item that times the next frames in play mode and logs the average frame rate</summary>
public class FrameRateRecorder : MonoBehaviour
{
    private const int Frames = 500;

    private int recorded;
    private float totalSeconds;

    [MenuItem("Tools/Record frame rate")]
    private static void Record()
    {
        GameObject recorder = new("Frame rate recorder") { hideFlags = HideFlags.HideInHierarchy };
        recorder.AddComponent<FrameRateRecorder>();
        Debug.Log($"Frame rate: recording the next {Frames} frames");
    }

    /// <summary>Frames only run in play mode</summary>
    [MenuItem("Tools/Record frame rate", true)]
    private static bool CanRecord() => Application.isPlaying;

    private void Update()
    {
        // The frame the recorder was added in started before it, so it isn't counted
        if (recorded++ == 0)
            return;

        totalSeconds += Time.unscaledDeltaTime;
        if (recorded <= Frames)
            return;

        float mean = totalSeconds / Frames;
        Debug.Log($"Frame rate: {1 / mean:F1} FPS average over {Frames} frames ({mean * 1000:F2} ms per frame)");
        Destroy(gameObject);
    }
}
#endif
