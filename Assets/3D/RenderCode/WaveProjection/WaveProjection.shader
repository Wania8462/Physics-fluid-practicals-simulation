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
            float4 lightMap_TexelSize;  // Set by Unity: 1/width, 1/height, width, height
            float2 tankHalfSize;  // x and z
            float exposure;

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

            // Cubic B-spline filtering from four bilinear taps, so the coarse light map shows no cell edges
            float SampleSmooth(float2 uv)
            {
                float2 texel = uv * lightMap_TexelSize.zw - 0.5;
                float2 base = floor(texel);
                float2 f = texel - base;

                float2 f2 = f * f;
                float2 f3 = f2 * f;
                float2 w0 = (1 - 3 * f + 3 * f2 - f3) / 6;
                float2 w1 = (4 - 6 * f2 + 3 * f3) / 6;
                float2 w2 = (1 + 3 * f + 3 * f2 - 3 * f3) / 6;
                float2 w3 = f3 / 6;

                float2 s0 = w0 + w1;
                float2 s1 = w2 + w3;
                float2 h0 = (base - 0.5 + w1 / s0) * lightMap_TexelSize.xy;
                float2 h1 = (base + 1.5 + w3 / s1) * lightMap_TexelSize.xy;

                return s0.y * (s0.x * tex2D(lightMap, h0).r + s1.x * tex2D(lightMap, float2(h1.x, h0.y)).r)
                     + s1.y * (s0.x * tex2D(lightMap, float2(h0.x, h1.y)).r + s1.x * tex2D(lightMap, h1).r);
            }

            float4 frag(v2f i) : SV_Target
            {
                // Rolls bright light off towards white instead of clipping, so bands keep their shape
                float brightness = 1 - exp(-exposure * SampleSmooth(i.uv));
                return float4(brightness.xxx, 1);
            }
            ENDCG
        }
    }
}
