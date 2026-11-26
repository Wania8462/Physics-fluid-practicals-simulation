using NUnit.Framework;
using Unity.Mathematics;

/// <summary>FluidMath3D: the fluid formulas used on the C# side</summary>
public class FluidMathTests
{
    private const float Tolerance = 1e-5f;

    #region Kernels
    /// <summary>The kernel factors at radius 1 are the textbook constants</summary>
    [Test]
    public void KernelFactors_RadiusOne()
    {
        Assert.AreEqual(315 / (64 * math.PI), FluidMath3D.Poly6Factor(1), Tolerance);
        Assert.AreEqual(45 / math.PI, FluidMath3D.SpikyGradientFactor(1), Tolerance);
        Assert.AreEqual(32 / math.PI, FluidMath3D.CohesionFactor(1), Tolerance);
    }

    /// <summary>Doubling the radius divides the factors by 2⁹ or 2⁶</summary>
    [Test]
    public void KernelFactors_ScaleWithRadius()
    {
        Assert.AreEqual(FluidMath3D.Poly6Factor(1) / 512, FluidMath3D.Poly6Factor(2), Tolerance);
        Assert.AreEqual(FluidMath3D.SpikyGradientFactor(1) / 64, FluidMath3D.SpikyGradientFactor(2), Tolerance);
        Assert.AreEqual(FluidMath3D.CohesionFactor(1) / 512, FluidMath3D.CohesionFactor(2), Tolerance);
    }

    /// <summary>A radius of 0 has no finite factor</summary>
    [Test]
    public void KernelFactors_ZeroRadius_Infinite()
    {
        Assert.IsTrue(float.IsPositiveInfinity(FluidMath3D.Poly6Factor(0)));
        Assert.IsTrue(float.IsPositiveInfinity(FluidMath3D.SpikyGradientFactor(0)));
        Assert.IsTrue(float.IsPositiveInfinity(FluidMath3D.CohesionFactor(0)));
    }
    #endregion

    #region Particles and columns
    /// <summary>A particle stands for a cube of the spacing</summary>
    [TestCase(1, 1)]
    [TestCase(3, 27)]
    [TestCase(0, 0)]
    public void ParticleVolume_IsSpacingCubed(float spacing, float volume)
    {
        Assert.AreEqual(volume, FluidMath3D.ParticleVolume(spacing), Tolerance);
    }

    /// <summary>Compression only counts density above rest</summary>
    [TestCase(0.5f, 0)]
    [TestCase(1, 0)]
    [TestCase(1.2f, 0.2f)]
    [TestCase(0, 0)]
    public void Compression_OnlyAboveRest(float relative, float expected)
    {
        Assert.AreEqual(expected, FluidMath3D.Compression(relative), Tolerance);
    }

    /// <summary>A particle counts fully at the column's centre, falling to nothing at its edge and beyond</summary>
    [TestCase(0, 0, 1)]
    [TestCase(0.5f, 0, 0.5f)]
    [TestCase(0.5f, 0.5f, 0.25f)]
    [TestCase(1, 0, 0)]
    [TestCase(-2, 0, 0)]
    public void ColumnWeight_FallsLinearly(float x, float z, float expected)
    {
        Assert.AreEqual(expected, FluidMath3D.ColumnWeight(new float2(x, z), 1), Tolerance);
    }

    /// <summary>Height per particle is its volume spread over the column's floor</summary>
    [Test]
    public void HeightPerParticle_IsVolumeOverArea()
    {
        Assert.AreEqual(2, FluidMath3D.HeightPerParticle(2, 4), Tolerance);
        Assert.IsTrue(float.IsInfinity(FluidMath3D.HeightPerParticle(2, 0)));
    }
    #endregion

    #region Time
    /// <summary>After one smoothing time the blend is about two thirds of the way</summary>
    [Test]
    public void SmoothingBlend_OneSmoothingTime_TwoThirds()
    {
        Assert.AreEqual(1 - math.exp(-1), FluidMath3D.SmoothingBlend(0.5f, 0.5f), Tolerance);
    }

    /// <summary>No time passing doesn't move at all</summary>
    [Test]
    public void SmoothingBlend_ZeroDeltaTime_Zero()
    {
        Assert.AreEqual(0, FluidMath3D.SmoothingBlend(0, 1), Tolerance);
    }

    /// <summary>No smoothing time, or a negative one, jumps straight to the target</summary>
    [TestCase(0)]
    [TestCase(-1)]
    public void SmoothingBlend_NoSmoothing_One(float smoothingTime)
    {
        Assert.AreEqual(1, FluidMath3D.SmoothingBlend(0.1f, smoothingTime));
    }

    /// <summary>The amplitude whose peak acceleration is exactly the limit</summary>
    [Test]
    public void AmplitudeForAcceleration_MatchesPeakAcceleration()
    {
        float omega = 2 * math.PI * 0.5f;
        Assert.AreEqual(1, FluidMath3D.AmplitudeForAcceleration(omega * omega, 0.5f), Tolerance);
        Assert.AreEqual(0, FluidMath3D.AmplitudeForAcceleration(0, 0.5f), Tolerance);
    }

    /// <summary>At 0 Hz any amplitude works</summary>
    [Test]
    public void AmplitudeForAcceleration_ZeroFrequency_Infinite()
    {
        Assert.IsTrue(float.IsPositiveInfinity(FluidMath3D.AmplitudeForAcceleration(1, 0)));
    }

    /// <summary>The phase moves 2π per period and wraps back to 0</summary>
    [Test]
    public void AdvancePhase_WrapsAtTwoPi()
    {
        Assert.AreEqual(math.PI / 2, FluidMath3D.AdvancePhase(0, 1, 0.25f), Tolerance);
        Assert.AreEqual(math.PI / 2, FluidMath3D.AdvancePhase(1.5f * math.PI, 1, 0.5f), 1e-4f);
        Assert.AreEqual(0, FluidMath3D.AdvancePhase(0, 1, 0), Tolerance);
    }

    /// <summary>Going backwards still has to land in 0 to 2π, as its summary says</summary>
    [Test, Category("KnownBug")]
    public void AdvancePhase_Negative_StaysInRange()
    {
        float phase = FluidMath3D.AdvancePhase(0, 1, -0.25f);
        Assert.That(phase, Is.InRange(0, 2 * math.PI), "A negative phase wasn't wrapped");
    }
    #endregion

    #region Vectors
    /// <summary>A vector longer than the limit is cut to it, keeping its direction</summary>
    [Test]
    public void ClampLength_TooLong_Shortened()
    {
        float3 clamped = FluidMath3D.ClampLength(new float3(3, 4, 0), 2.5f);
        Assert.AreEqual(2.5f, math.length(clamped), Tolerance);
        Assert.AreEqual(0.6f, clamped.x / 2.5f, Tolerance);
    }

    /// <summary>A vector inside the limit, or exactly at it, is unchanged</summary>
    [TestCase(10)]
    [TestCase(5)]
    public void ClampLength_WithinLimit_Unchanged(float max)
    {
        float3 clamped = FluidMath3D.ClampLength(new float3(3, 4, 0), max);
        Assert.AreEqual(3, clamped.x, Tolerance);
        Assert.AreEqual(4, clamped.y, Tolerance);
    }

    /// <summary>A zero vector or a zero limit gives zero</summary>
    [Test]
    public void ClampLength_Zero_GivesZero()
    {
        Assert.AreEqual(float3.zero, FluidMath3D.ClampLength(float3.zero, 5));
        Assert.AreEqual(0, math.length(FluidMath3D.ClampLength(new float3(1, 0, 0), 0)), Tolerance);
    }
    #endregion
}
