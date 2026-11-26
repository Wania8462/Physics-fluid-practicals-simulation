using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static PlayModeHelpers;

/// <summary>The waves practical scene, run for real: window limits, the wave dipper and depth readings</summary>
public class WavesSceneTests
{
    private WindowManager windows;
    private SimulationAPI api;

    [UnitySetUp]
    public IEnumerator LoadPractical()
    {
        yield return LoadScene("WavesPractical");
        windows = Find<WindowManager>();
        api = Find<SimulationAPI>();
    }

    /// <summary>Depth meter windows stop at half the meters, as each window can show two rays</summary>
    [UnityTest]
    public IEnumerator DepthMeters_StopAtLimit()
    {
        for (int i = 0; i < 10; i++)
        {
            windows.CreateDepthMeterWindow();
            yield return null;
        }

        Assert.AreEqual(DepthMeters.MaxMeters / 2, WindowsStartingWith("Depth meter").Length);
        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>Rulers stop at the visualiser's limit</summary>
    [UnityTest]
    public IEnumerator Rulers_StopAtLimit()
    {
        for (int i = 0; i < 10; i++)
        {
            windows.CreateRulerWindow();
            yield return null;
        }

        Assert.AreEqual(Visualiser.MaxRulers, WindowsStartingWith("Ruler").Length);
    }

    /// <summary>There's only one wave generator, however many times it's asked for</summary>
    [UnityTest]
    public IEnumerator WaveWindow_OnlyOne()
    {
        for (int i = 0; i < 5; i++)
        {
            windows.CreateWaveWindow();
            yield return null;
        }

        Assert.AreEqual(1, Windows("Wave generator").Length);
    }

    /// <summary>A freed slot can be used again after closing a limited window</summary>
    [UnityTest]
    public IEnumerator ClosedDepthMeter_CanReopen()
    {
        for (int i = 0; i < DepthMeters.MaxMeters / 2; i++)
            windows.CreateDepthMeterWindow();
        yield return null;

        yield return Close(Windows("Depth meter 2"));
        windows.CreateDepthMeterWindow();
        Assert.AreEqual(1, Windows("Depth meter 2").Length);
    }

    /// <summary>The dipper can be switched on at its limits, run and taken out without errors</summary>
    [UnityTest]
    public IEnumerator Dipper_RunsAtLimits()
    {
        windows.CreateWaveWindow();
        yield return null;

        api.SetDipper(true, 0.25f, 0.06f);
        yield return Frames(60);
        api.SetDipper(true, 0.05f, 0);
        yield return Frames(30);
        api.SetDipper(false, 0.15f, 0.03f);
        yield return Frames(30);

        yield return Close(Windows("Wave generator"));
        yield return Frames(10);
        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>A depth meter reads a real depth inside the tank once the fluid has been sampled</summary>
    [UnityTest]
    public IEnumerator DepthMeter_ReadsDepthInsideTank()
    {
        windows.CreateDepthMeterWindow();
        yield return Frames(120);

        float depth = api.GetDepthMeterReading(0);
        float tankHeight = api.ToMetres(api.GetDepthRayLength());
        Assert.That(depth, Is.GreaterThan(0).And.LessThanOrEqualTo(tankHeight));
        Assert.Greater(api.GetDepthMeterHistory(0, 30).Length, 0, "No readings were recorded");
    }

    /// <summary>Stopwatches open and close without limit problems</summary>
    [UnityTest]
    public IEnumerator Stopwatches_Spam_NoErrors()
    {
        for (int i = 0; i < 20; i++)
        {
            windows.CreateStopwatchWindow();
            yield return null;
        }

        yield return Close(Windows("Stopwatch"));
        LogAssert.NoUnexpectedReceived();
    }
}
