using System.Collections;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;
using static PlayModeHelpers;

/// <summary>The buoyancy practical scene, run for real: spawning limits, window spam and typing into windows</summary>
public class BuoyancySceneTests
{
    private const string Warning = "Warning";

    private WindowManager windows;
    private SimulationAPI api;

    [UnitySetUp]
    public IEnumerator LoadPractical()
    {
        yield return LoadScene("BuoyancyPractical");
        windows = Find<WindowManager>();
        api = Find<SimulationAPI>();
    }

    /// <summary>The open warning's text, null when there's none</summary>
    private static string WarningText()
    {
        Window warning = Windows(Warning).LastOrDefault();
        return warning ? string.Join("\n", warning.fields.Where(f => f.fieldType == FieldType.Text).Select(f => f.name)) : null;
    }

    #region Object limit
    /// <summary>Spawning past the body limit adds nothing, warns, and greys out the button until there's room again</summary>
    [UnityTest]
    public IEnumerator Objects_PastLimit_WarnAndGreyOut()
    {
        UnityEngine.UI.Button button = ButtonCalling(nameof(WindowManager.CreateObjectWindow));
        Assert.AreEqual(1, Alpha(button), "The button starts greyed out");

        for (int i = 0; i < RigidBodies3D.MaxBodies && api.CanAddBody(); i++)
        {
            windows.CreateObjectWindow();
            yield return null;
        }

        Assert.AreEqual(RigidBodies3D.MaxBodies, BodyCount(api));
        yield return null;
        Assert.AreEqual(WindowManager.GreyedAlpha, Alpha(button), "The full object button isn't greyed out");

        windows.CreateObjectWindow();
        Assert.AreEqual(RigidBodies3D.MaxBodies, BodyCount(api), "A body was added past the limit");
        StringAssert.Contains($"Can't spawn more than {RigidBodies3D.MaxBodies} objects", WarningText());

        yield return Close(Windows("New object")[0]);
        yield return null;
        Assert.IsTrue(api.CanAddBody());
        Assert.AreEqual(1, Alpha(button), "The button stayed greyed out after a body was removed");
    }

    /// <summary>Spawning many times in one frame still stops at the limit</summary>
    [UnityTest]
    public IEnumerator Objects_ManyInOneFrame_StopAtLimit()
    {
        for (int i = 0; i < 50; i++)
            windows.CreateObjectWindow();

        Assert.AreEqual(RigidBodies3D.MaxBodies, BodyCount(api));
        Assert.LessOrEqual(Windows("New object").Length, RigidBodies3D.MaxBodies);
        Assert.AreEqual(1, Windows(Warning).Length, "The same warning opened more than once");
        yield return Frames(30);
    }
    #endregion

    #region Scale limit
    /// <summary>Only two scales can be out, a third click warns and the button greys out</summary>
    [UnityTest]
    public IEnumerator Scales_PastLimit_WarnAndGreyOut()
    {
        UnityEngine.UI.Button button = ButtonCalling(nameof(WindowManager.CreateScaleWindow));

        for (int i = 0; i < RigidBodies3D.MaxScales; i++)
        {
            windows.CreateScaleWindow();
            yield return null;
        }

        Assert.AreEqual(1, Windows("Scale 1").Length);
        Assert.AreEqual(1, Windows("Scale 2").Length);
        yield return null;
        Assert.AreEqual(WindowManager.GreyedAlpha, Alpha(button));

        windows.CreateScaleWindow();
        Assert.AreEqual(0, Windows("Scale 3").Length);
        StringAssert.Contains($"Can't place more than {RigidBodies3D.MaxScales} scales", WarningText());

        yield return Close(Windows("Scale 1")[0]);
        yield return null;
        Assert.AreEqual(1, Alpha(button));

        windows.CreateScaleWindow();
        Assert.AreEqual(1, Windows("Scale 1").Length, "The freed scale couldn't be placed again");
    }

    /// <summary>Scales share the body limit, so a tank full of objects greys out the scale button too</summary>
    [UnityTest]
    public IEnumerator ObjectsAndScales_ShareLimit()
    {
        windows.CreateScaleWindow();
        windows.CreateScaleWindow();
        while (api.CanAddBody())
            windows.CreateObjectWindow();
        yield return null;

        Assert.AreEqual(RigidBodies3D.MaxBodies, BodyCount(api));
        Assert.AreEqual(WindowManager.GreyedAlpha, Alpha(ButtonCalling(nameof(WindowManager.CreateObjectWindow))));
        Assert.AreEqual(WindowManager.GreyedAlpha, Alpha(ButtonCalling(nameof(WindowManager.CreateScaleWindow))));

        yield return Close(Windows("Scale 2")[0]);
        yield return null;
        Assert.AreEqual(1, Alpha(ButtonCalling(nameof(WindowManager.CreateObjectWindow))));
        Assert.AreEqual(1, Alpha(ButtonCalling(nameof(WindowManager.CreateScaleWindow))));
    }
    #endregion

    #region Window spam
    /// <summary>Clicking every button many times opens windows without errors, and closing them all frees every body</summary>
    [UnityTest]
    public IEnumerator EveryButton_FiftyTimes_NoErrors()
    {
        int bodiesBefore = BodyCount(api);

        for (int i = 0; i < 50; i++)
        {
            windows.CreateFluidWindow();
            windows.CreateGravityWindow();
            windows.CreateVisualisationWindow();
            windows.CreateObjectWindow();
            windows.CreateScaleWindow();
            windows.OnSideMenuClick();
            yield return null;
        }

        Assert.AreEqual(50, Windows("Fluid properties").Length);
        Assert.AreEqual(50, Windows("Gravity properties").Length);
        Assert.LessOrEqual(BodyCount(api), RigidBodies3D.MaxBodies);
        Assert.LessOrEqual(Windows(Warning).Length, 1);

        yield return Frames(30);
        yield return Close(Object.FindObjectsByType<Window>(FindObjectsSortMode.None));
        yield return Frames(5);

        Assert.AreEqual(bodiesBefore, BodyCount(api), "Closing the windows didn't remove their bodies");
        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>The same warning asked for twice shows once</summary>
    [UnityTest]
    public IEnumerator SameWarningTwice_ShowsOnce()
    {
        windows.CreateWarningWindow("Test warning");
        windows.CreateWarningWindow("Test warning");
        Assert.AreEqual(1, Windows(Warning).Length);

        windows.CreateWarningWindow("Another warning");
        yield return Frames(30);
        Assert.AreEqual("Another warning", WarningText(), "A new warning didn't replace the old one");
    }

    /// <summary>A warning with an empty message still opens and closes</summary>
    [UnityTest]
    public IEnumerator EmptyWarning_StillCloses()
    {
        windows.CreateWarningWindow("");
        yield return null;
        yield return Close(Windows(Warning));
        LogAssert.NoUnexpectedReceived();
    }
    #endregion

    #region Typing into windows
    /// <summary>Gravity typed past its limit is pulled back to it, and text is ignored</summary>
    [UnityTest]
    public IEnumerator GravityWindow_TypedValues_ReachSimulation()
    {
        windows.CreateGravityWindow();
        yield return null;
        FieldUI gravity = FieldOf(Windows("Gravity properties").Last(), 0);

        Type(gravity, "30");
        yield return Frames(2);
        Assert.AreEqual(25, api.GetGravity(), 1e-3f);
        Assert.AreEqual(FieldUI.TooBigMessage, gravity.ErrorMessage);

        Type(gravity, "abc");
        yield return Frames(2);
        Assert.AreEqual(25, api.GetGravity(), 1e-3f);
        Assert.AreEqual(FieldUI.NotANumberMessage, gravity.ErrorMessage);

        Type(gravity, "1.62");
        yield return Frames(2);
        Assert.AreEqual(1.62f, api.GetGravity(), 1e-3f);
    }

    /// <summary>Density and viscosity typed outside their limits are pulled back to them</summary>
    [UnityTest]
    public IEnumerator FluidWindow_Limits()
    {
        windows.CreateFluidWindow();
        yield return null;
        Window window = Windows("Fluid properties").Last();

        Type(FieldOf(window, 0), "15000");
        Type(FieldOf(window, 1), "-1");
        yield return Frames(2);

        Assert.AreEqual(1500, api.GetDensity(), 1e-2f);
        Assert.AreEqual(0, api.GetViscosity(), 1e-5f);
    }

    /// <summary>A material fixes the density, so typing a mass changes the volume, and the mass follows the volume's limits</summary>
    [UnityTest]
    public IEnumerator ObjectWindow_LockedToMaterial_MassLimitedByVolume()
    {
        const int Mass = 0, Volume = 1;
        const float Wood = 600;

        windows.CreateObjectWindow();
        yield return null;
        Window window = Windows("New object").Last();

        // 80 kg of wood would need more than the 10 L the volume allows
        Type(FieldOf(window, Mass), "80");
        yield return Frames(2);
        Assert.AreEqual(10, window.fields[Volume].value, 1e-3f);
        Assert.AreEqual(Wood * 10 / 1000, window.fields[Mass].value, 1e-3f);

        Type(FieldOf(window, Mass), "0.6");
        yield return Frames(2);
        Assert.AreEqual(1, window.fields[Volume].value, 1e-3f);
        Assert.AreEqual(0.6f, window.fields[Mass].value, 1e-3f);

        Type(FieldOf(window, Volume), "kg");
        yield return Frames(2);
        Assert.AreEqual(1, window.fields[Volume].value, 1e-3f, "Text changed the volume");
    }
    #endregion

    #region Simulation
    /// <summary>A dropped body stays inside the tank and its readings stay in range</summary>
    [UnityTest]
    public IEnumerator Body_StaysInTankWithSaneReadings()
    {
        int id = api.AddBody(BodyShape3D.Cube, 0.002f, 1.2f);
        float submerged = 0;
        for (int i = 0; i < 120; i++)
        {
            submerged = api.GetSubmergedPercent(id);
            yield return null;
        }

        Vector3 position = api.GetBodyPosition(id);
        float3 half = Find<SimulationManager>().Tank.halfSize;
        Assert.IsTrue(math.all(math.abs((float3)position) <= half), $"The body left the tank: {position}");
        Assert.That(submerged, Is.InRange(0f, 100f));
        Assert.IsTrue(api.GetBodyBounds(id).size.x > 0);
    }

    /// <summary>A scale's reading is a real, non-negative number once the simulation has run</summary>
    [UnityTest]
    public IEnumerator Scale_ReadingIsFinite()
    {
        api.AddScale(0);
        float reading = 0;
        for (int i = 0; i < 60; i++)
        {
            reading = api.GetScaleReading(0);
            yield return null;
        }

        Assert.IsFalse(float.IsNaN(reading) || float.IsInfinity(reading));
        Assert.GreaterOrEqual(reading, -1e-3f);
    }

    /// <summary>Pausing stops simulation time</summary>
    [UnityTest]
    public IEnumerator Pause_StopsTime()
    {
        api.SetPaused(true);
        yield return null;
        float time = api.GetSimulationTime();
        yield return Frames(10);
        Assert.AreEqual(time, api.GetSimulationTime());

        api.SetPaused(false);
        yield return Frames(10);
        Assert.Greater(api.GetSimulationTime(), time);
    }
    #endregion
}
