Shader "SwanUnderwater/Ocean Entry Warp"
{
    Properties
    {
        _Opacity("Transition opacity", Range(0,1)) = 0
        _FlowTime("Flow time", Float) = 0
        _Rush("Rush", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+900" "RenderType"="Transparent" }
        Cull Front ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Opacity, _FlowTime, _Rush;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float3 direction:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A input)
            {
                V output; UNITY_SETUP_INSTANCE_ID(input); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.direction=input.positionOS.xyz;
                return output;
            }
            half4 frag(V input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 d=normalize(input.direction);
                float2 p=d.xy / max(.25,d.z+.32);
                float r=length(p), a=atan2(p.y,p.x), t=_FlowTime;
                float front=smoothstep(-.3,.65,d.z);
                float twist=a+.22*sin(r*2.4-t*.6)+r*.65-t*.22;
                float ribbons=pow(saturate(.5+.5*sin(twist*7+r*1.3-t*.8)),7);
                float fine=pow(saturate(.5+.5*sin(twist*31.0+sin(a*3.0)*2.0)),28);
                float stream=pow(saturate(.5+.5*sin(r*12.0-t*(7.0+_Rush*9.0)+a*.6)),6);
                float aurora=saturate(.5+.5*sin(a*2.0+r*3.5-t*.75));
                half3 navy=half3(.002,.009,.060);
                half3 blue=lerp(half3(.012,.08,.38),half3(.020,.30,.64),aurora);
                half3 tint=lerp(half3(.16,.055,.32),half3(.045,.70,.88),saturate(.5+.5*sin(twist*2.0+t*.35)));
                half3 color=navy+blue*(.13+ribbons*.65)*front;
                color+=tint*(ribbons*.30+fine*stream*(.3+_Rush*.8))*front;
                // A calm, luminous vanishing point; no full-screen white flashes.
                color+=half3(.04,.18,.30)*exp(-r*r*28.0)*front;
                return half4(color,saturate(_Opacity));
            }
            ENDHLSL
        }
    }
}
