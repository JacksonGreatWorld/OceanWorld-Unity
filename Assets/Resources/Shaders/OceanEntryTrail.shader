Shader "SwanUnderwater/Ocean Entry Trail"
{
    Properties { _Tint("Tint",Color)=(.08,.7,1,.65) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+20" "RenderType"="Transparent" }
        Cull Off ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial) half4 _Tint; CBUFFER_END
            struct A { float4 p:POSITION; float4 c:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 p:SV_POSITION; half4 c:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i) { V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.p=TransformObjectToHClip(i.p.xyz);o.c=i.c*_Tint;o.uv=i.uv;return o; }
            half4 frag(V i):SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); half edge=saturate(1-abs(i.uv.y*2-1));return half4(i.c.rgb,i.c.a*edge*edge); }
            ENDHLSL
        }
    }
}
