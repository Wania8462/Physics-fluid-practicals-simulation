Shader "Custom/Body3D"
{
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }

        // Shared by both passes: every body is posed in the vertex shader straight from the body buffers
        CGINCLUDE
        #include "UnityCG.cginc"
        #include "UnityIndirect.cginc"
        #include "Assets/3D/SimulationCode/RigidBodyMath3D.hlsl"

        #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs

        StructuredBuffer<RigidBodyState> BodyStates;
        StructuredBuffer<RigidBodyProperties> BodyProperties;
        uint drawShape;         // Shape of this draw's mesh, ShapeCube, ShapeSphere or ShapeScale
        uint drawPart;          // For ShapeScale, 0 draws the base and 1 the pan

        struct v2f
        {
            float4 pos : SV_POSITION;
            float3 normal : TEXCOORD0;
        };

        v2f vert(appdata_base v, uint instanceID : SV_InstanceID)
        {
            RigidBodyProperties props = BodyProperties[instanceID];
            RigidBodyState state = BodyStates[instanceID];

            // Bodies of the other shape get zero size, so all their vertices meet at one point and nothing is rasterised
            float visible = props.shape == drawShape;
            float isBox = (props.shape == ShapeCube) + (props.shape == ShapeScale);
            float3 halfExtents = lerp(props.size.xxx, props.size.xyz, isBox);

            // The cube and sphere meshes are unit sized (cube edge 1, sphere diameter 1), so twice a vertex spans the half
            // extents or radius. The pan's cylinder has diameter 1 and height 2, and sits on top of the base
            float isPan = drawPart == 1;
            float3 meshScale = lerp(2 * halfExtents, float3(2 * ScalePanRadius, ScalePanHalfHeight, 2 * ScalePanRadius), isPan) * visible;
            float3 offset = float3(0, props.size.y + ScalePanHalfHeight, 0) * isPan;

            // Positions are centres of mass, so the shape is shifted by the body-local centre of mass before rotating
            float3 local = v.vertex.xyz * meshScale + offset * visible - props.centerOfMass.xyz;
            float3 world = state.position.xyz + QuaternionRotate(state.rotation, local);

            v2f o;
            o.pos = mul(UNITY_MATRIX_VP, float4(world, 1));
            // Scaling along the cube's own axes leaves its face normals unchanged, so only the rotation applies
            o.normal = QuaternionRotate(state.rotation, v.normal);
            return o;
        }
        ENDCG

        // No LightMode tag, so the Built-in forward renderer draws this pass with the opaque geometry, into the depth buffer
        Pass
        {
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Share of the colour faces turned away from the light keep
            static const float ambient = 0.3;

            float4 color;
            float3 lightDirection;  // Direction the light travels in, a global set by RenderManager

            float4 frag(v2f i) : SV_Target
            {
                float diffuse = saturate(dot(normalize(i.normal), -normalize(lightDirection)));
                return color * (ambient + (1 - ambient) * diffuse);
            }
            ENDCG
        }

        // Depth only. ScreenSpaceFluidRenderer draws it into the fluid's own depth buffer, so bodies hide the fluid behind
        // them. ShadowCaster is the Built-in pipeline's depth-only pass type, so the forward renderer doesn't also draw it
        // alongside the pass above
        Pass
        {
            Name "DepthOnly"    // BodyRenderer.DepthPassName
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
