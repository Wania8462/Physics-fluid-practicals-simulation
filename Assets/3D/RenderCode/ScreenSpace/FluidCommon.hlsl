// Shared by the screen-space fluid shaders. Depth here is distance in front of the camera

#ifndef FLUID_COMMON_INCLUDED
#define FLUID_COMMON_INCLUDED

#include "UnityCG.cginc"

// Depth where there's no fluid. Compare with >=, the cleared value can come out slightly larger
#define FLUID_NO_DEPTH 1e7

// Camera matrices, set by ScreenSpaceFluidRenderer
float4x4 _FluidProj;
float4x4 _FluidInvProj;
float4x4 _FluidCamToWorld;

// Point sampling, so depth isn't blended across edges
SamplerState sampler_point_clamp;

float4 SamplePoint(Texture2D source, float2 uv)
{
    return source.SampleLevel(sampler_point_clamp, uv, 0);
}

// Converts a depth to a depth-buffer value
float EyeDepthToRaw(float eyeDepth)
{
    return (1 / eyeDepth - _ZBufferParams.w) / _ZBufferParams.z;
}

// Direction from the camera through uv. Times a depth it gives the view-space position
float3 ViewRay(float2 uv)
{
    float3 ray = mul(_FluidInvProj, float4(uv * 2 - 1, 0, 1)).xyz;
    return ray / -ray.z;
}

// How many pixels one world unit covers at a depth
float PixelsPerUnit(float eyeDepth, float textureHeight)
{
    return _FluidProj._m11 * textureHeight / (2 * eyeDepth);
}

#endif
