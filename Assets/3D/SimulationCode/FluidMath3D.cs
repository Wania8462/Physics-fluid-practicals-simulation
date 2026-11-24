using Unity.Mathematics;

/// <summary>Fluid math for the C# side. FluidMath3D.hlsl has the GPU side</summary>
public static class FluidMath3D
{
    #region Smoothing kernels
    /// <summary>Scales Poly6 in FluidMath3D.hlsl so it integrates to 1 over its radius</summary>
    public static float Poly6Factor(float radius) => 315 / (64 * math.PI * math.pow(radius, 9));

    /// <summary>Scales SpikyGradient in FluidMath3D.hlsl</summary>
    public static float SpikyGradientFactor(float radius) => 45 / (math.PI * math.pow(radius, 6));

    /// <summary>Scales CohesionKernel in FluidMath3D.hlsl (Akinci et al. 2013)</summary>
    public static float CohesionFactor(float radius) => 32 / (math.PI * math.pow(radius, 9));
    #endregion

    #region Particles
    /// <summary>Fluid one particle stands for: a cube of the spawn spacing</summary>
    public static float ParticleVolume(float spacing) => spacing * spacing * spacing;

    /// <summary>How far a particle is squashed past rest density, 0 when it's below</summary>
    /// <param name="relativeDensity">Density over rest density</param>
    public static float Compression(float relativeDensity) => math.max(relativeDensity - 1, 0);
    #endregion

    #region Fluid columns
    /// <summary>
    /// How much of a particle counts towards a square column: 1 at its centre, falling linearly to 0 at its edge
    /// on each axis, so the total changes smoothly as particles cross it. The weights of one particle
    /// integrate to halfWidth², the column's effective area
    /// </summary>
    /// <param name="offset">From the column's centre to the particle, across the floor</param>
    public static float ColumnWeight(float2 offset, float halfWidth)
    {
        float2 weight = math.saturate(1 - math.abs(offset) / halfWidth);
        return weight.x * weight.y;
    }

    /// <summary>Height of fluid that a weight of one particle adds to a column</summary>
    /// <param name="columnArea">Floor area the particle's weight is spread over</param>
    public static float HeightPerParticle(float spacing, float columnArea) => ParticleVolume(spacing) / columnArea;
    #endregion

    #region Time
    /// <summary>
    /// How far to ease towards a target this step, so it takes the same time whatever the step length.
    /// After smoothingTime it's about two thirds of the way there. 0 jumps straight to it
    /// </summary>
    public static float SmoothingBlend(float deltaTime, float smoothingTime)
        => smoothingTime > 0 ? 1 - math.exp(-deltaTime / smoothingTime) : 1;

    /// <summary>Largest amplitude a sine wave at this frequency can have without its acceleration passing maxAcceleration</summary>
    public static float AmplitudeForAcceleration(float maxAcceleration, float frequency)
    {
        float omega = 2 * math.PI * frequency;
        return maxAcceleration / (omega * omega);
    }

    /// <summary>The phase after deltaTime at this frequency, wrapped to 0 to 2π</summary>
    public static float AdvancePhase(float phase, float frequency, float deltaTime)
        => (phase + 2 * math.PI * frequency * deltaTime) % (2 * math.PI);
    #endregion

    /// <summary>The vector, shortened to maxLength if it's longer</summary>
    public static float3 ClampLength(float3 vector, float maxLength)
        => math.normalizesafe(vector) * math.min(math.length(vector), maxLength);
}
