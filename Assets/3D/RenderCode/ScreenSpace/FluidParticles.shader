Shader "Hidden/ScreenSpaceFluid/Particles"
{
    SubShader
    {
        // Every particle is a camera-facing quad shaded as a sphere. ScreenSpaceFluidRenderer draws them with
        // DrawProcedural, 6 vertices per particle and no mesh
        Cull Off

        CGINCLUDE
        #include "Assets/3D/RenderCode/ScreenSpace/FluidCommon.hlsl"

        StructuredBuffer<float4> Positions;
        float particleRenderRadius;

        // The quad's two triangles, as corners of a square from -1 to 1
        static const float2 Corners[6] =
        {
            float2(-1, -1), float2(1, -1), float2(-1, 1),
            float2(-1, 1), float2(1, -1), float2(1, 1)
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
            float2 corner : TEXCOORD0;
            nointerpolation float centreEyeDepth : TEXCOORD1;
        };

        v2f vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
        {
            float3 centre = Positions[instanceID].xyz;
            float2 corner = Corners[vertexID];

            // Offsetting the corners in view space keeps the quad facing the camera
            float3 viewCentre = mul(UNITY_MATRIX_V, float4(centre, 1)).xyz;
            float3 viewPosition = viewCentre + float3(corner * particleRenderRadius, 0);

            v2f o;
            // A particle with a NaN or infinite position is moved outside clip space, so no NaN depth reaches the smoothing
            o.pos = all(isfinite(centre)) ? mul(UNITY_MATRIX_P, float4(viewPosition, 1)) : float4(2, 2, 2, 1);
            o.corner = corner;
            o.centreEyeDepth = -viewCentre.z;
            return o;
        }
        ENDCG

        // Eye depth of the nearest sphere surface, in both channels: R gets smoothed, G keeps the raw depth
        Pass
        {
            Name "Depth"    // ScreenSpaceFluidRenderer.DepthPassName
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 frag(v2f i, out float depth : SV_Depth) : SV_Target
            {
                float r2 = dot(i.corner, i.corner); // equivalent to x^2 + y^2
                clip(1 - r2);

                // The sphere surface bulges towards the camera from the quad. Clamped to the near plane, so a sphere around
                // the camera doesn't give a depth at or behind it
                float eyeDepth = max(i.centreEyeDepth - sqrt(1 - r2) * particleRenderRadius, _ProjectionParams.y);
                depth = EyeDepthToRaw(eyeDepth);
                return float4(eyeDepth, eyeDepth, 0, 0);
            }
            ENDCG
        }

        // Length of the view ray inside each sphere, added up over every particle in front of the bodies. It is in world
        // units, so the absorption coefficients are per world unit
        Pass
        {
            Name "Thickness"    // ScreenSpaceFluidRenderer.ThicknessPassName
            ZWrite Off
            ZTest LEqual
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 frag(v2f i) : SV_Target
            {
                // Zero outside the circle, so no discard is needed
                float chord = 2 * sqrt(saturate(1 - dot(i.corner, i.corner))) * particleRenderRadius;
                return float4(chord, 0, 0, 0);
            }
            ENDCG
        }
    }
}
