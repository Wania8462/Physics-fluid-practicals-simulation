Shader "Custom/ForceArrow3D"
{
    SubShader
    {
        // Drawn on top of everything, so arrows show inside bodies and fluid
        Tags { "Queue"="Overlay" "RenderType"="Opaque" }

        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "UnityIndirect.cginc"
            #include "Assets/3D/SimulationCode/RigidBodyMath3D.hlsl"

            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs

            // The forces, then their resultant, matching BodyForce in Visualiser.cs
            #define ArrowsPerBody 4
            static const uint ResultantArrow = 3;

            static const float ambient = 0.3;

            StructuredBuffer<RigidBodyState> BodyStates;
            StructuredBuffer<float4> BodyForces;
            uint shownForces;       // Bit i set when BodyForce i is drawn
            float arrowScale;
            float maxArrowLength;
            float width;            // Head radius, the shaft is half as wide
            float headLength;
            float4 colors[ArrowsPerBody];
            float3 lightDirection;  // Global, set by RenderManager

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float4 color : TEXCOORD1;
            };

            v2f vert(appdata_base v, uint instanceID : SV_InstanceID)
            {
                uint body = instanceID / ArrowsPerBody;
                uint arrow = instanceID % ArrowsPerBody;

                float3 force = 0;
                for (uint f = 0; f < BodyForceCount; f++)
                    force += BodyForces[body * BodyForceCount + f].xyz * (f == arrow || arrow == ResultantArrow);

                // Hidden arrows get zero length
                float magnitude = length(force);
                float arrowLength = min(magnitude * arrowScale, maxArrowLength) * ((shownForces >> arrow) & 1);

                // An arrow shorter than its head is all head
                float head = min(headLength, arrowLength);
                float radius = width * head / headLength;
                float along = v.vertex.y * (arrowLength - head) + v.texcoord.y * head;

                // Point the mesh along the force
                float3 axis = magnitude > 1e-9 ? force / magnitude : float3(0, 1, 0);
                float3 side = normalize(cross(abs(axis.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0), axis));
                float3 front = cross(side, axis);

                float3 world = BodyStates[body].position.xyz + axis * along + (side * v.vertex.x + front * v.vertex.z) * radius;

                v2f o;
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1));
                o.normal = side * v.normal.x + axis * v.normal.y + front * v.normal.z;
                o.color = colors[arrow];
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float diffuse = saturate(dot(normalize(i.normal), -normalize(lightDirection)));
                return i.color * (ambient + (1 - ambient) * diffuse);
            }
            ENDCG
        }
    }
}
