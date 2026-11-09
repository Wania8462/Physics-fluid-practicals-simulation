Shader "Hidden/ScreenSpaceFluid/Particles"
{
    SubShader
    {
        // Draws each particle as a sphere, into the depth or the thickness texture
        Cull Off

        CGINCLUDE
        #include "Assets/3D/RenderCode/ScreenSpace/FluidCommon.hlsl"

        StructuredBuffer<float4> Positions;
        float particleRenderRadius;
        float thicknessScale;  // Particle volume / sphere volume

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

            float3 viewCentre = mul(UNITY_MATRIX_V, float4(centre, 1)).xyz;
            float3 viewPosition = viewCentre + float3(corner * particleRenderRadius, 0);

            v2f o;
            // Broken (NaN) particles are moved off screen so they don't spoil the image
            o.pos = all(isfinite(centre)) ? mul(UNITY_MATRIX_P, float4(viewPosition, 1)) : float4(2, 2, 2, 1);
            o.corner = corner;
            o.centreEyeDepth = -viewCentre.z;
            return o;
        }
        ENDCG

        // Depth of the nearest sphere. R gets smoothed, G keeps the raw depth
        Pass
        {
            Name "Depth"
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 frag(v2f i, out float depth : SV_Depth) : SV_Target
            {
                float r2 = dot(i.corner, i.corner);
                clip(1 - r2);

                float eyeDepth = max(i.centreEyeDepth - sqrt(1 - r2) * particleRenderRadius, _ProjectionParams.y);
                depth = EyeDepthToRaw(eyeDepth);
                return float4(eyeDepth, eyeDepth, 0, 0);
            }
            ENDCG
        }

        // Thickness: how much fluid each pixel looks through
        Pass
        {
            Name "Thickness"
            ZWrite Off
            ZTest LEqual
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 frag(v2f i) : SV_Target
            {
                float chord = 2 * sqrt(saturate(1 - dot(i.corner, i.corner))) * particleRenderRadius;
                return float4(chord * thicknessScale, 0, 0, 0);
            }
            ENDCG
        }
    }
}
