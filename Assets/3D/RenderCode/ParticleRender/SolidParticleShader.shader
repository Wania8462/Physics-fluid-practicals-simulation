Shader "Custom/SolidParticle3D"
{
    Properties
    {
        solidColor ("Solid Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "UnityIndirect.cginc"

            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs

            StructuredBuffer<float4> Positions;
            float4 solidColor;

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata_base v, uint instanceID : SV_InstanceID)
            {
                v2f o;

                // Camera-facing billboard, as in ParticleShader
                float3 worldPos = Positions[instanceID].xyz
                    + v.vertex.x * UNITY_MATRIX_V[0].xyz
                    + v.vertex.y * UNITY_MATRIX_V[1].xyz;

                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1));

                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return solidColor;
            }
            ENDCG
        }
    }
}
