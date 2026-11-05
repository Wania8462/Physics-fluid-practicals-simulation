Shader "Hidden/ScreenSpaceFluid/Smooth"
{
    SubShader
    {
        // Blurs the fluid depth and thickness, horizontally then vertically
        Cull Off
        ZWrite Off
        ZTest Always

        CGINCLUDE
        #include "Assets/3D/RenderCode/ScreenSpace/FluidCommon.hlsl"

        // Most pixels sampled on each side
        #define MAX_RADIUS 64

        // Read by name, Blit's _MainTex gave the wrong texture in Play mode
        Texture2D _FluidDepthTex;  // R: smoothed depth, G: raw depth
        Texture2D _FluidDepthTemp;
        Texture2D _FluidThicknessTex;
        Texture2D _FluidThicknessTemp;

        float smoothingRadius;  // World units
        float maxScreenRadius;  // Fraction of the target's height
        float depthRangeSigma;  // World units

        // Blur radius in pixels for a pixel at this depth, and how fast the blur fades out
        int BlurRadius(float eyeDepth, float height, out float falloff)
        {
            float radius = min(smoothingRadius * PixelsPerUnit(eyeDepth, height), maxScreenRadius * height);

            falloff = -4.5 / max(radius * radius, 1e-4);
            return min((int)ceil(radius), MAX_RADIUS);
        }

        // Smooths depth but keeps edges sharp, so separate blobs don't merge
        float4 Bilateral(Texture2D source, float2 uv, float2 direction)
        {
            float width, height;
            source.GetDimensions(width, height);

            float4 centre = SamplePoint(source, uv);
            if (centre.g >= FLUID_NO_DEPTH)
                return centre;

            float falloff;
            int radius = BlurRadius(centre.g, height, falloff);
            float rangeFalloff = -0.5 / max(depthRangeSigma * depthRangeSigma, 1e-6);
            float2 texelStep = direction / float2(width, height);

            // Pixels with no fluid get no weight
            float sum = 0;
            float weightSum = 0;
            [loop] for (int x = -radius; x <= radius; x++)
            {
                float2 tap = SamplePoint(source, uv + texelStep * x).rg;
                float depthDifference = tap.g - centre.g;
                float weight = exp(x * x * falloff + depthDifference * depthDifference * rangeFalloff);
                sum += tap.r * weight;
                weightSum += weight;
            }

            return float4(sum / weightSum, centre.g, 0, 0);
        }

        // Plain blur, for thickness
        float4 Gauss(Texture2D source, float2 uv, float2 direction)
        {
            float width, height;
            source.GetDimensions(width, height);

            float4 centre = SamplePoint(source, uv);
            float eyeDepth = SamplePoint(_FluidDepthTex, uv).g;
            if (eyeDepth >= FLUID_NO_DEPTH)
                return centre;

            float falloff;
            int radius = BlurRadius(eyeDepth, height, falloff);
            float2 texelStep = direction / float2(width, height);

            float sum = 0;
            float weightSum = 0;
            [loop] for (int x = -radius; x <= radius; x++)
            {
                float weight = exp(x * x * falloff);
                sum += SamplePoint(source, uv + texelStep * x).r * weight;
                weightSum += weight;
            }

            return float4(sum / weightSum, 0, 0, 0);
        }
        ENDCG

        Pass
        {
            Name "BilateralH"

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                return Bilateral(_FluidDepthTex, i.uv, float2(1, 0));
            }
            ENDCG
        }

        Pass
        {
            Name "BilateralV"

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                return Bilateral(_FluidDepthTemp, i.uv, float2(0, 1));
            }
            ENDCG
        }

        Pass
        {
            Name "GaussH"

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                return Gauss(_FluidThicknessTex, i.uv, float2(1, 0));
            }
            ENDCG
        }

        Pass
        {
            Name "GaussV"

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                return Gauss(_FluidThicknessTemp, i.uv, float2(0, 1));
            }
            ENDCG
        }
    }
}
