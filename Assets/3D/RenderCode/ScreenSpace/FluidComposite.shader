Shader "Hidden/ScreenSpaceFluid/Composite"
{
    SubShader
    {
        // Shades the fluid over the scene, or shows one stage for debugging
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "Composite"

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "Assets/3D/RenderCode/ScreenSpace/FluidCommon.hlsl"

            // Values of ScreenSpaceFluidRenderer.DebugView
            static const int ViewRawDepth = 1;
            static const int ViewSmoothDepth = 2;
            static const int ViewNormals = 3;
            static const int ViewThickness = 4;
            static const int ViewSmoothThickness = 5;

            // How much light water reflects looking straight at it
            static const float WaterReflectance = 0.02;

            // Approximate colours of the default skybox
            static const float3 SkyZenith = float3(0.16, 0.32, 0.65);
            static const float3 SkyHorizon = float3(0.6, 0.68, 0.78);
            static const float3 GroundColor = float3(0.12, 0.11, 0.1);

            // Size and brightness of the sun highlight
            static const float SunSharpness = 500;
            static const float SunIntensity = 30;

            Texture2D _FluidSceneTex;
            Texture2D _FluidDepthTex;  // R: smoothed depth, G: raw depth
            Texture2D _FluidThicknessTex;
            SamplerState sampler_linear_clamp;

            float3 extinction;
            float refractionStrength;
            float maxRefractionOffset;
            float3 lightDirection;  // Global, set by RenderManager
            int debugView;
            float depthDisplayScale;
            float thicknessDisplayScale;

            float4 SampleLinear(Texture2D source, float2 uv)
            {
                return source.SampleLevel(sampler_linear_clamp, uv, 0);
            }

            // Shows a value as grey, for the debug views
            float4 DebugShade(float value, float scale)
            {
                return float4(GammaToLinearSpace(saturate(value / scale).xxx), 1);
            }

            // Sky and sun colour in a direction, for reflections
            float3 Environment(float3 direction)
            {
                float3 sky = lerp(SkyHorizon, SkyZenith, sqrt(saturate(direction.y)));
                float sun = pow(saturate(dot(direction, -normalize(lightDirection))), SunSharpness) * SunIntensity;
                return lerp(GroundColor, sky + sun, smoothstep(-0.05, 0, direction.y));
            }

            float3 SurfacePosition(float2 uv)
            {
                return ViewRay(uv) * SamplePoint(_FluidDepthTex, uv).r;
            }

            // Step to the neighbouring point on the same surface
            float3 SurfaceDifference(float3 centre, float2 uv, float2 texel)
            {
                float3 forward = SurfacePosition(uv + texel) - centre;
                float3 backward = centre - SurfacePosition(uv - texel);
                return abs(forward.z) < abs(backward.z) ? forward : backward;
            }

            // Surface normal from the smoothed depth
            float3 ViewNormal(float2 uv, float eyeDepth)
            {
                float width, height;
                _FluidDepthTex.GetDimensions(width, height);

                float3 centre = ViewRay(uv) * eyeDepth;
                float3 right = SurfaceDifference(centre, uv, float2(1 / width, 0));
                float3 up = SurfaceDifference(centre, uv, float2(0, 1 / height));
                return normalize(cross(right, up));
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float4 scene = SampleLinear(_FluidSceneTex, i.uv);
                float4 depth = SamplePoint(_FluidDepthTex, i.uv);
                float thickness = SampleLinear(_FluidThicknessTex, i.uv).r;

                if (debugView == ViewThickness || debugView == ViewSmoothThickness)
                    return thickness > 0 ? DebugShade(thickness, thicknessDisplayScale) : scene;

                if (depth.g >= FLUID_NO_DEPTH)
                    return scene;

                if (debugView == ViewRawDepth || debugView == ViewSmoothDepth)
                    return DebugShade(debugView == ViewRawDepth ? depth.g : depth.r, depthDisplayScale);

                float3 viewNormal = ViewNormal(i.uv, depth.r);
                float3 normal = mul((float3x3)_FluidCamToWorld, viewNormal);

                if (debugView == ViewNormals)
                    return float4(GammaToLinearSpace(normal * 0.5 + 0.5), 1);

                // Water reflects more when seen at a low angle
                float3 viewDirection = normalize(mul((float3x3)_FluidCamToWorld, ViewRay(i.uv)));
                float fresnel = WaterReflectance + (1 - WaterReflectance) * pow(saturate(1 + dot(normal, viewDirection)), 5);
                float3 reflected = Environment(reflect(viewDirection, normal));

                // Refraction: bend the view of the scene behind, more through thicker fluid
                float2 uvPerUnit = float2(_FluidProj._m00, _FluidProj._m11) / (2 * depth.r);
                float2 offset = clamp(-viewNormal.xy * thickness * refractionStrength * uvPerUnit, -maxRefractionOffset, maxRefractionOffset);

                // Thicker fluid tints the scene behind it more
                float3 refracted = SampleLinear(_FluidSceneTex, i.uv + offset).rgb * exp(-thickness * extinction);

                return float4(lerp(refracted, reflected, fresnel), 1);
            }
            ENDCG
        }
    }
}
