Shader "SwanUnderwater/EnvironmentVeil"
{
    Properties { _Opacity("Opacity",Range(0,1))=0 }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Opacity;
            CBUFFER_END
            struct A { float4 p:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 p:SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.p=TransformObjectToHClip(v.p.xyz); return o; }
            half4 frag(V i):SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); return half4(.012,.075,.105,_Opacity); }
            ENDHLSL
        }
    }
}
