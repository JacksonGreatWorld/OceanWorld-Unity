Shader "SwanUnderwater/HandWake"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
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
            struct V { float4 p:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i){V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.p=TransformObjectToHClip(i.p.xyz);o.uv=i.uv;o.color=i.color;return o;}
            half4 frag(V i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv=i.uv*2-1;
                float r=length(uv);
                float ring=exp(-pow((r-0.70)*28,2))+0.4*exp(-pow((r-0.52)*35,2));
                float arc=0.55+0.45*sin(uv.x*9+uv.y*6);
                return half4(i.color.rgb,ring*arc*i.color.a);
            }
            ENDHLSL
        }
    }
}
