Shader "Custom/Particle3D"
{
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
            StructuredBuffer<float4> Colors;

            struct v2f
            {
                float4 pos : SV_POSITION;
                nointerpolation float4 color : COLOR0;
            };

            v2f vert(appdata_base v, uint instanceID : SV_InstanceID)
            {
                v2f o;

                // The circle mesh lies in the XY plane; spanning it by the camera's
                // right/up axes turns it into a camera-facing billboard
                float3 worldPos = Positions[instanceID].xyz
                    + v.vertex.x * UNITY_MATRIX_V[0].xyz
                    + v.vertex.y * UNITY_MATRIX_V[1].xyz;

                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1));
                o.color = Colors[instanceID];

                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}
