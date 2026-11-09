Shader "Custom/Body3D"
{
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }

        CGINCLUDE
        #include "UnityCG.cginc"
        #include "UnityIndirect.cginc"
        #include "Assets/3D/SimulationCode/RigidBodyMath3D.hlsl"

        #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs

        StructuredBuffer<RigidBodyState> BodyStates;
        StructuredBuffer<RigidBodyProperties> BodyProperties;
        uint drawShape;
        uint drawPart;  // Scales: 0 = base, 1 = pan

        struct v2f
        {
            float4 pos : SV_POSITION;
            float3 normal : TEXCOORD0;
            float worldY : TEXCOORD1;
            // Bottom and height of the body in the world, and whether it gets depth lines
            nointerpolation float3 depthSpan : TEXCOORD2;
        };

        v2f vert(appdata_base v, uint instanceID : SV_InstanceID)
        {
            RigidBodyProperties props = BodyProperties[instanceID];
            RigidBodyState state = BodyStates[instanceID];

            // Bodies of other shapes shrink to nothing, so they aren't drawn
            float visible = props.shape == drawShape;
            float isBox = (props.shape == ShapeCube) + (props.shape == ShapeScale);
            float3 halfExtents = lerp(props.size.xxx, props.size.xyz, isBox);

            // Scale the unit mesh to the body's size. The pan sits on top of a scale's base
            float isPan = drawPart == 1;
            float3 meshScale = lerp(2 * halfExtents, float3(2 * ScalePanRadius, ScalePanHalfHeight, 2 * ScalePanRadius), isPan) * visible;
            float3 offset = float3(0, props.size.y + ScalePanHalfHeight, 0) * isPan;

            float3 local = v.vertex.xyz * meshScale + offset * visible - props.centerOfMass.xyz;
            float3 world = state.position.xyz + QuaternionRotate(state.rotation, local);

            v2f o;
            o.pos = mul(UNITY_MATRIX_VP, float4(world, 1));
            // Only rotation changes the normals
            o.normal = QuaternionRotate(state.rotation, v.normal);
            o.worldY = world.y;

            // How far the rotated body reaches up and down from its centre
            float centreY = state.position.y + QuaternionRotate(state.rotation, -props.centerOfMass.xyz).y;
            float boxHalfHeight = dot(abs(QuaternionRotateInverse(state.rotation, float3(0, 1, 0))), halfExtents);
            float halfHeight = lerp(props.size.x, boxHalfHeight, isBox);
            o.depthSpan = float3(centreY - halfHeight, 2 * halfHeight, props.shape != ShapeScale);
            return o;
        }
        ENDCG

        // Lit, opaque bodies
        Pass
        {
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            static const float ambient = 0.3;
            static const float DepthLineCount = 4;

            float4 color;
            float3 lightDirection;  // Global, set by RenderManager
            // Globals, set by Visualiser
            float depthLines;
            float4 depthLineColor;
            float depthLineWidth;  // In pixels

            float4 frag(v2f i) : SV_Target
            {
                float diffuse = saturate(dot(normalize(i.normal), -normalize(lightDirection)));
                float lighting = ambient + (1 - ambient) * diffuse;

                // Evenly spaced horizontal lines between the body's bottom and top, a fixed number of pixels thick
                float t = (i.worldY - i.depthSpan.x) / i.depthSpan.y * (DepthLineCount + 1);
                float pixelsAway = abs(t - round(t)) / max(fwidth(t), 1e-5);
                float onLine = saturate(depthLineWidth / 2 - pixelsAway + 0.5) * (t > 0.5) * (t < DepthLineCount + 0.5);

                // The lines are lit like the body, so they shade with it
                return lerp(color, depthLineColor, onLine * depthLines * i.depthSpan.z) * lighting;
            }
            ENDCG
        }

        // Depth only, so bodies hide the screen-space fluid behind them
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ColorMask 0

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth

            float4 fragDepth(v2f i) : SV_Target
            {
                return 0;
            }
            ENDCG
        }
    }
}
