using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The screen-space fluid's depth blur, run on the GPU through FluidSmooth.shader's horizontal pass.
/// Checks the 2026-11-09 fix: flat surfaces stay flat and body edges aren't smeared
/// </summary>
public class FluidBlurTests
{
    private const string ShaderPath = "Assets/3D/RenderCode/ScreenSpace/FluidSmooth.shader";
    private const int Width = 1000, Height = 1080, Row = Height / 2;
    private const float Spacing = 3;

    private Material material;

    [SetUp]
    public void CreateMaterial()
    {
        Assume.That(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "No graphics device");

        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        Assert.IsNotNull(shader, $"Missing {ShaderPath}");

        // The renderer's settings: radius 2 spacings, depth sigma 0.75 spacings, at most 6% of the screen
        material = new Material(shader);
        material.SetFloat("smoothingRadius", 2 * Spacing);
        material.SetFloat("depthRangeSigma", 0.75f * Spacing);
        material.SetFloat("maxScreenRadius", 0.06f);
        Matrix4x4 projection = Matrix4x4.identity;
        projection.m11 = 1.73f;
        material.SetMatrix("_FluidProj", projection);
    }

    [TearDown]
    public void DestroyMaterial()
    {
        if (material)
            Object.DestroyImmediate(material);
    }

    /// <summary>Blurs one horizontal depth profile, the same on every row, and returns the middle row</summary>
    private float[] BlurHorizontally(System.Func<int, float> depthAt)
    {
        // Raw depth = smoothed depth, as before the first pass
        float[] pixels = new float[Width * Height * 2];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
                pixels[(y * Width + x) * 2] = pixels[(y * Width + x) * 2 + 1] = depthAt(x);
        }

        Texture2D source = new(Width, Height, TextureFormat.RGFloat, false, true) { filterMode = FilterMode.Point };
        source.SetPixelData(pixels, 0);
        source.Apply();
        RenderTexture target = new(Width, Height, 0, RenderTextureFormat.RGFloat, RenderTextureReadWrite.Linear);

        try
        {
            material.SetTexture("_FluidDepthTex", source);
            Graphics.Blit(source, target, material, material.FindPass("BilateralH"));

            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(target, 0, TextureFormat.RGFloat);
            request.WaitForCompletion();
            Assert.IsFalse(request.hasError, "GPU readback failed");

            NativeArray<float> data = request.GetData<float>();
            float[] row = new float[Width];
            for (int x = 0; x < Width; x++)
                row[x] = data[(Row * Width + x) * 2];
            return row;
        }
        finally
        {
            Object.DestroyImmediate(source);
            target.Release();
            Object.DestroyImmediate(target);
        }
    }

    /// <summary>A tilted flat surface has to come out straight, at any distance</summary>
    [TestCase(300)]
    [TestCase(180)]
    [TestCase(90)]
    public void TiltedPlane_StaysStraight(float nearDepth)
    {
        float[] row = BlurHorizontally(x => nearDepth + 0.15f * (x - 500));

        float worstBend = 0;
        for (int x = 200; x <= 800; x++)
            worstBend = Mathf.Max(worstBend, Mathf.Abs(row[x - 1] - 2 * row[x] + row[x + 1]));

        // ponytail: tolerances came from a JS copy of the shader, retune if the GPU's float rounding differs
        Assert.Less(worstBend, 0.01f, "The blur bent a flat surface");
    }

    /// <summary>How far the blur moved the depth near a step in it</summary>
    private float LargestChangeAtStep(float stepInSpacings)
    {
        float Depth(int x) => x < 500 ? 200 : 200 + stepInSpacings * Spacing;
        float[] row = BlurHorizontally(Depth);

        float largest = 0;
        for (int x = 470; x <= 530; x++)
            largest = Mathf.Max(largest, Mathf.Abs(row[x] - Depth(x)));
        return largest;
    }

    /// <summary>A small step, as between neighbouring particles, is softened a little</summary>
    [Test]
    public void SmallStep_SoftenedALittle()
    {
        Assert.AreEqual(0.5f, LargestChangeAtStep(1.5f), 0.1f);
    }

    /// <summary>A step of a body's edge is kept sharp</summary>
    [TestCase(3, 0.01f)]
    [TestCase(5, 0.001f)]
    public void BodyEdgeStep_KeptSharp(float stepInSpacings, float most)
    {
        Assert.Less(LargestChangeAtStep(stepInSpacings), most, "The blur smeared fluid across a body's edge");
    }

    /// <summary>Pixels with no fluid are passed through untouched</summary>
    [Test]
    public void NoFluid_Untouched()
    {
        float[] row = BlurHorizontally(_ => 1e7f);
        Assert.AreEqual(1e7f, row[500]);
    }
}
