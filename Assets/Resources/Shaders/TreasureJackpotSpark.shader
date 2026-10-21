Shader "SwanUnderwater/TreasureJackpotSpark"
{
    Properties { _SparkColor("Spark", Color)=(1,.7,.2,1) _SparkKind("Halo", Float)=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Overlay+45" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha One Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            UNITY_INSTANCING_BUFFER_START(SparkProps)
                UNITY_DEFINE_INSTANCED_PROP(half4,_SparkColor)
                UNITY_DEFINE_INSTANCED_PROP(float,_SparkKind)
            UNITY_INSTANCING_BUFFER_END(SparkProps)
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v); Varyings o=(Varyings)0;
                UNITY_TRANSFER_INSTANCE_ID(v,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.uv=v.uv*2-1; return o;
            }
            half4 frag(Varyings i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p=abs(i.uv); half core=pow(saturate(1-length(p)),3);
                half cross=pow(saturate(1-p.x),24)*pow(saturate(1-p.y),2)+pow(saturate(1-p.y),24)*pow(saturate(1-p.x),2);
                half halo=UNITY_ACCESS_INSTANCED_PROP(SparkProps,_SparkKind);
                half4 color=UNITY_ACCESS_INSTANCED_PROP(SparkProps,_SparkColor);
                color.a*=lerp(saturate(cross+core*.6),core,halo); return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
