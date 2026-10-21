Shader "SwanUnderwater/TreasureJackpotGold"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Overlay+50" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; half3 normalVS:TEXCOORD0; half3 color:COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o=(Varyings)0; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
                o.normalVS=TransformWorldToViewDir(TransformObjectToWorldNormal(v.normalOS));
                o.color=v.color.rgb; return o;
            }
            half4 frag(Varyings i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 n=normalize(i.normalVS);
                half key=saturate(dot(n,normalize(half3(-.42,.65,1))));
                half spec=pow(saturate(dot(n,normalize(half3(-.23,.33,1)))),32);
                half strip=pow(saturate(1-abs(dot(n,normalize(half3(.76,.28,.18))))),18);
                half edge=pow(1-abs(n.z),3);
                half3 gold=i.color*half3(1.3,1.04,.56)*(.75+.45*key);
                gold+=half3(1,.82,.35)*strip*.48+half3(1,.93,.63)*spec*.75+half3(.45,.20,.025)*edge;
                return half4(gold,1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
