#define PI 3.14159265359

float Distance(float4 p1, float4 p2);
float DistanceSq(float4 p1, float4 p2);
float Pow3(float x);

// Positions and velocities are float4 with w = 0 (16 byte GPU alignment),
// only xyz carries simulation data

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

// Poly6 density kernel, normalised so it integrates to 1. factor = 315 / (64π h⁹). Zero beyond the radius
float Poly6(float distanceSq, float radiusSq, float factor)
{
    return factor * Pow3(max(radiusSq - distanceSq, 0.0));
}

// Gradient of the spiky kernel with respect to the particle, which points towards the neighbour. factor = 45 / (π h⁶)
float4 SpikyGradient(float distance, float radius, float factor, float4 towardsNeighbour)
{
    float overlap = radius - distance;
    return factor * overlap * overlap * towardsNeighbour;
}

// Density a wall adds when everything beyond it counts as fluid at rest: the Poly6 kernel integrated over the half
// space past the wall. x is the distance to the wall over the radius, clamped to [0, 1]. 0.5 at the wall, 0 at x = 1
float3 WallDensity(float3 x)
{
    float3 x2 = x * x;
    float3 integral = x * (1.0 + x2 * (-4.0 / 3.0 + x2 * (6.0 / 5.0 + x2 * (-4.0 / 7.0 + x2 / 9.0))));
    return 0.5 - 315.0 / 256.0 * integral;
}

// How fast WallDensity grows as a particle moves towards the wall
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
