Shader "SwanUnderwater/Ocean Environment Transition"
{
    Properties { _Opacity("Opacity",Range(0,1))=0 _FlowTime("Flow time",Float)=0 _Rush("Rush",Range(0,1))=0 _PatternDepth("Pattern depth",Float)=2.5 }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Opacity, _FlowTime, _Rush, _PatternDepth;
            CBUFFER_END
            struct A { float4 vertex:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float2 passage:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS=TransformObjectToHClip(v.vertex.xyz); o.passage=v.vertex.xy/max(.01,_PatternDepth); return o; }
            half4 frag(V i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // A shared physical pattern, not a separate radial centre in each eye's raster.
                // Perspective-correct interpolation respects asymmetric XR eye projections.
                float2 p=i.passage;
                float r=length(p), a=atan2(p.y,p.x), t=_FlowTime;
                float twist=a+.08*sin(a*3+t*.45)+r*.18;
                float beam=pow(saturate(.5+.5*sin(twist*23+sin(a*7)*.7)),14);
                float phase=frac(log2(r+.08)*.38-t*(.65+_Rush*1.1)+sin(a*11)*.35);
                float streak=smoothstep(.04,.23,phase)*(1-smoothstep(.40,.72,phase));
                float fine=pow(saturate(.5+.5*sin(a*61+sin(a*5+t*.5)*2)),26);
                float light=(beam*streak+fine*.13)*smoothstep(.02,.25,r)*(.25+_Rush*.75);
                half3 navy=half3(.004,.023,.085);
                half3 blue=lerp(half3(.015,.20,.45),half3(.035,.48,.65),saturate(.5+.5*sin(a*3+t*.3)));
                half3 color=navy+blue*light+half3(.025,.10,.19)*exp(-r*r*3);
                return half4(color,saturate(_Opacity));
            }
            ENDHLSL
        }
    }
}
