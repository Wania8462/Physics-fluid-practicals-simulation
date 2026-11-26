using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>RenderAPI: the UI's settings for what the visualiser draws</summary>
public class RenderAPITests
{
    private GameObject host;
    private RenderAPI api;
    private Visualiser visualiser;

    [SetUp]
    public void CreateApi()
    {
        host = new GameObject("Render test");
        visualiser = host.AddComponent<Visualiser>();
        api = host.AddComponent<RenderAPI>();
        SimulationRig.SetReference(api, "visualiser", visualiser);
    }

    [TearDown]
    public void DestroyApi() => Object.DestroyImmediate(host);

    /// <summary>A positive arrow scale is passed on and read back</summary>
    [TestCase(0.002f)]
    [TestCase(float.Epsilon)]
    [TestCase(1000)]
    public void SetArrowScale_Positive_Stored(float scale)
    {
        api.SetArrowScale(scale);
        Assert.AreEqual(scale, api.GetArrowScale());
    }

    /// <summary>An arrow scale of 0 or less is rejected and the old one kept</summary>
    [TestCase(0)]
    [TestCase(-1)]
    public void SetArrowScale_NotPositive_Throws(float scale)
    {
        float before = api.GetArrowScale();
        Assert.Throws<ArgumentOutOfRangeException>(() => api.SetArrowScale(scale));
        Assert.AreEqual(before, api.GetArrowScale());
    }

    /// <summary>A newton scale of 0 or less is rejected, a positive one is passed on</summary>
    [Test]
    public void SetNewtonScale_Validates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => api.SetNewtonScale(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => api.SetNewtonScale(-3));
        api.SetNewtonScale(16310);
        Assert.AreEqual(16310, visualiser.forcePerNewton);
    }

    /// <summary>Not a number isn't a scale</summary>
    [Test, Category("KnownBug")]
    public void SetScales_NotANumber_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => api.SetArrowScale(float.NaN), "A NaN arrow scale was accepted");
        Assert.Throws<ArgumentOutOfRangeException>(() => api.SetNewtonScale(float.NaN), "A NaN newton scale was accepted");
    }

    /// <summary>Each force's arrows switch on and off on their own</summary>
    [Test]
    public void DrawForce_TogglesOnlyThatForce()
    {
        api.DrawForce(BodyForce.Gravity, true);
        api.DrawForce(BodyForce.Gravity, false);
        api.DrawForce(BodyForce.Gravity, true);

        Assert.IsTrue(api.IsForceDrawn(BodyForce.Gravity));
        foreach (BodyForce force in Enum.GetValues(typeof(BodyForce)))
        {
            if (force != BodyForce.Gravity)
                Assert.IsFalse(api.IsForceDrawn(force));
        }
    }

    /// <summary>A force that doesn't exist can't be drawn</summary>
    [Test]
    public void DrawForce_UnknownForce_Throws()
    {
        Assert.Catch(() => api.DrawForce((BodyForce)99, true));
    }

    /// <summary>Force values and depth lines switch on and off</summary>
    [Test]
    public void ValuesAndDepthLines_Toggle()
    {
        api.DrawForceValues(true);
        api.DrawDepthLines(true);
        Assert.IsTrue(api.AreForceValuesDrawn());
        Assert.IsTrue(api.AreDepthLinesDrawn());

        api.DrawForceValues(false);
        api.DrawDepthLines(false);
        Assert.IsFalse(api.AreForceValuesDrawn());
        Assert.IsFalse(api.AreDepthLinesDrawn());
    }

    /// <summary>A ruler is drawn centred halfway along its length, and hiding it clears it</summary>
    [Test]
    public void ShowRuler_CentredAlongLength()
    {
        Quaternion rotation = Quaternion.Euler(0, 90, 0);
        api.ShowRuler(0, Vector3.zero, rotation, new Vector3(12, 1.5f, 100), 2);

        Vector3 centre = visualiser.rulers[0].Value.body.GetColumn(3);
        Assert.AreEqual(50, centre.x, 1e-3f);
        Assert.AreEqual(0, centre.z, 1e-3f);

        api.HideRuler(0);
        Assert.IsNull(visualiser.rulers[0]);
    }

    /// <summary>Rulers are numbered 0 to MaxRulers - 1</summary>
    [TestCase(-1)]
    [TestCase(Visualiser.MaxRulers)]
    public void ShowRuler_OutOfRange_Throws(int ruler)
    {
        Assert.Catch(() => api.ShowRuler(ruler, Vector3.zero, Quaternion.identity, Vector3.one, 1));
    }

    /// <summary>A depth ray stores its centre and length, and hiding it clears it</summary>
    [Test]
    public void ShowDepthRay_StoresCentreAndLength()
    {
        api.ShowDepthRay(2, new Vector3(1, 2, 3), 120);
        Assert.AreEqual(new Vector4(1, 2, 3, 120), visualiser.depthRays[2]);

        api.HideDepthRay(2);
        Assert.IsNull(visualiser.depthRays[2]);
    }

    /// <summary>Scale text and labels are stored per scale, and null text draws nothing</summary>
    [Test]
    public void ScaleTextAndLabel()
    {
        api.SetScaleText(1, "12.3 N");
        api.ShowScaleLabel(1, true);
        Assert.AreEqual("12.3 N", visualiser.scaleTexts[1]);
        Assert.IsTrue(visualiser.scaleLabelsShown[1]);

        api.SetScaleText(1, null);
        Assert.IsNull(visualiser.scaleTexts[1]);
        Assert.Catch(() => api.SetScaleText(RigidBodies3D.MaxScales, "x"));
    }

    /// <summary>Without a render manager the ripple tank view is just off</summary>
    [Test]
    public void IsWaveProjectionOn_NoRenderManager_False()
    {
        Assert.IsFalse(api.IsWaveProjectionOn());
    }
}
