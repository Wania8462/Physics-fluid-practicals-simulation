Shader "Hidden/WaveProjection/Plane"
{
    SubShader
    {
        // The white card under the tank, lit by the light map
        Tags { "RenderType" = "Opaque" }
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D lightMap;
            float2 tankHalfSize;  // x and z
            float planeBrightness;

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;

                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.uv = (world.xz + tankHalfSize) / (2 * tankHalfSize);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return float4((planeBrightness * tex2D(lightMap, i.uv).r).xxx, 1);
            }
            ENDCG
        }
    }
}
