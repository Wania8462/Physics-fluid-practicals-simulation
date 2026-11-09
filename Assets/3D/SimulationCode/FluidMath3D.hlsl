// Positions and velocities are float4 with w unused, for GPU alignment

#define PI 3.14159265359

float Distance(float4 p1, float4 p2);
float DistanceSq(float4 p1, float4 p2);
float Pow3(float x);

// Velocity change from viscosity between two particles
float4 ViscosityImpulse(float deltaTime, float highViscosity, float lowViscosity, float relativeDistance, float inwardVelocity, float4 unitVector)
{
    return deltaTime *
        (1.0 - relativeDistance) *
        (highViscosity * inwardVelocity + (lowViscosity * inwardVelocity)) *
        unitVector;
}

float4 UnitVector(float4 initialVector, float4 finalVector)
{
    return (finalVector - initialVector) / Distance(initialVector, finalVector);
}

float4 UnitVector(float4 initialVector, float4 finalVector, float distance)
{
    return (finalVector - initialVector) / distance;
}

// Smoothing kernel for density: largest at the centre, zero at the radius
float Poly6(float distanceSq, float radiusSq, float factor)
{
    return factor * Pow3(max(radiusSq - distanceSq, 0.0));
}

// Direction and strength of the push between two particles
float4 SpikyGradient(float distance, float radius, float factor, float4 towardsNeighbour)
{
    float overlap = radius - distance;
    return factor * overlap * overlap * towardsNeighbour;
}

// Cohesion kernel (Akinci et al. 2013): pulls neighbours together, but pushes closer than about 0.28 of the radius so
// particles can't clump. factor = 32 / (π h⁹)
float CohesionKernel(float distance, float radius, float factor)
{
    float spline = Pow3((radius - distance) * distance);
    if (2.0 * distance <= radius)
        spline = 2.0 * spline - Pow3(radius * radius) / 64.0;
    return factor * spline;
}

// Density a wall adds to a nearby particle, as if there were fluid beyond it
float3 WallDensity(float3 x)
{
    float3 x2 = x * x;
    float3 integral = x * (1.0 + x2 * (-4.0 / 3.0 + x2 * (6.0 / 5.0 + x2 * (-4.0 / 7.0 + x2 / 9.0))));
    return 0.5 - 315.0 / 256.0 * integral;
}

// How that wall density changes as the particle moves towards the wall
float3 WallDensityGradient(float3 x, float radius)
{
    float3 y = 1.0 - x * x;
    return 315.0 / (256.0 * radius) * y * y * y * y;
}

float DistanceSq(float4 p1, float4 p2)
{
    float3 d = p2.xyz - p1.xyz;
    return dot(d, d);
}

float Distance(float4 p1, float4 p2)
{
    return sqrt(DistanceSq(p1, p2));
}

float Pow3(float x)
{
    return x * x * x;
}
