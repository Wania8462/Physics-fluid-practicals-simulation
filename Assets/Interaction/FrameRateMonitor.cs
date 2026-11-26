using UnityEngine;

/// <summary>Spots a frame rate that stays low, so the user can be told to lower the performance level</summary>
public class FrameRateMonitor
{
    /// <summary>Below this many frames per second counts as slow</summary>
    public const float MinFrameRate = 30;
    /// <summary>Seconds the frame rate has to stay slow before it's reported</summary>
    public const float SlowSeconds = 3;
    /// <summary>Frames longer than this are loading hitches, not the simulation being slow</summary>
    public const float HitchSeconds = 1;
    /// <summary>Seconds the frame time takes to ease most of the way to a new value, so one slow frame doesn't count</summary>
    private const float SmoothingSeconds = 0.5f;

    private float smoothedFrameTime = 1 / MinFrameRate / 2;
    private float slowFor;
    private bool reported;

    /// <summary>Call once a frame with the unscaled frame time. True only the frame the rate has been slow for long enough</summary>
    public bool Tick(float frameSeconds)
    {
        // Also rules out NaN
        if (reported || !(frameSeconds > 0 && frameSeconds <= HitchSeconds))
            return false;

        smoothedFrameTime = Mathf.Lerp(smoothedFrameTime, frameSeconds, FluidMath3D.SmoothingBlend(frameSeconds, SmoothingSeconds));
        slowFor = smoothedFrameTime > 1 / MinFrameRate ? slowFor + frameSeconds : 0;

        reported = slowFor >= SlowSeconds;
        return reported;
    }
}
