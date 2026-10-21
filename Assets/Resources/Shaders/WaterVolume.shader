Shader "SwanUnderwater/WaterVolume"
{
    Properties { _BaseColor ("Tint", Color) = (0.62,0.88,0.91,0.1) _Mist ("Mist instead of light", Float) = 0 _StructuredLight ("Broken sanctuary sunlight", Float) = 0 }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 p:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 p:SV_POSITION; float2 uv:TEXCOORD0; float3 world:TEXCOORD1; float4 color:COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor; float _Mist; float _StructuredLight;
            CBUFFER_END
            V vert(A i) { V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world=TransformObjectToWorld(i.p.xyz); o.p=TransformWorldToHClip(o.world); o.uv=i.uv; o.color=i.color; return o; }
            half4 frag(V i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float edge = pow(saturate(1-abs(i.uv.x*2-1)),2.5);
                float vertical = smoothstep(0,0.18,i.uv.y)*(1-smoothstep(0.72,1,i.uv.y));
                float distanceToEye = distance(i.world,GetCameraPositionWS());
                float nearFade = smoothstep(0.7,3.0,distanceToEye);
                float drift = 0.76+0.24*sin(i.uv.y*13.0+i.color.r*17+_Time.y*0.24);
                float alpha = edge*vertical*_BaseColor.a*i.color.a*nearFade;
                alpha *= lerp(drift,1,_Mist);
                // Slow world-space variation breaks the rigid laser-column appearance.
                // Both eyes sample the same field; the shafts never follow locomotion.
                float broken = .62 + .22*sin(i.world.x*.73+i.world.z*.49+_Time.y*.13)
                    + .16*sin(i.world.z*1.37-i.world.y*.32-_Time.y*.09);
                alpha *= lerp(1.0, saturate(broken), _StructuredLight);
                return half4(_BaseColor.rgb,alpha);
            }
            ENDHLSL
        }
    }
}
