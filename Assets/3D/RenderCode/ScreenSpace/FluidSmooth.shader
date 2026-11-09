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

        // Most samples on each side. Wider blurs space them out instead
        #define MAX_TAPS 24

        // Read by name, Blit's _MainTex gave the wrong texture in Play mode
        Texture2D _FluidDepthTex;  // R: smoothed depth, G: raw depth
        Texture2D _FluidDepthTemp;
        Texture2D _FluidThicknessTex;
        Texture2D _FluidThicknessTemp;

        float smoothingRadius;  // World units
        float maxScreenRadius;  // Fraction of the target's height
        float depthRangeSigma;  // World units

        // Samples per side and the pixel step between them, for a blur radius in pixels and how fast it fades out
        int BlurTaps(float pixelsPerUnit, float height, out float stride, out float falloff)
        {
            float radius = min(smoothingRadius * pixelsPerUnit, maxScreenRadius * height);
            falloff = -4.5 / max(radius * radius, 1e-4);

            int taps = clamp((int)ceil(radius), 1, MAX_TAPS);
            stride = radius / taps;
            return taps;
        }

        // Smooths depth but keeps edges sharp, so separate blobs don't merge
        float4 Bilateral(Texture2D source, float2 uv, float2 direction)
        {
            float width, height;
            source.GetDimensions(width, height);

            float4 centre = SamplePoint(source, uv);
            if (centre.g >= FLUID_NO_DEPTH)
                return centre;

            float pixelsPerUnit = PixelsPerUnit(centre.g, height);
            float stride, falloff;
            int taps = BlurTaps(pixelsPerUnit, height, stride, falloff);
            float2 texelStep = direction / float2(width, height);

            // Pixels with no fluid get no weight
            float sum = 0;
            float weightSum = 0;
            [loop] for (int t = -taps; t <= taps; t++)
            {
                float x = t * stride;
                float2 tap = SamplePoint(source, uv + texelStep * x).rg;
                float depthDifference = tap.g - centre.g;
                // Tolerates slopes up to 45 degrees, so tilted surfaces still blend
                float range = depthRangeSigma + abs(x) / pixelsPerUnit;
                float weight = exp(x * x * falloff - 0.5 * depthDifference * depthDifference / (range * range));
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

            float stride, falloff;
            int taps = BlurTaps(PixelsPerUnit(eyeDepth, height), height, stride, falloff);
            float2 texelStep = direction / float2(width, height);

            float sum = 0;
            float weightSum = 0;
            [loop] for (int t = -taps; t <= taps; t++)
            {
                float x = t * stride;
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
