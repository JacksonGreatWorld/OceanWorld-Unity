Shader "SwanUnderwater/SkyGuardian"
{
    Properties
    {
        _BaseMap("Original ray skin", 2D) = "white" {}
        _Centre("Mesh centre", Vector) = (0,0,0,0)
        _HalfExtent("Mesh extent", Vector) = (.5,.1,.5,0)
        _SwimTime("Slow swim clock", Float) = 0
        _SanctuaryTone("Blue-water sanctuary silhouette", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "CelestialGuardian"
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _Centre, _HalfExtent;
                float _SwimTime, _SanctuaryTone;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS:SV_POSITION;
                float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1;
                float2 uv:TEXCOORD2; float2 shape:TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings vert(Attributes i)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 p=i.positionOS.xyz;
                // Imported ray: wings along Z, head toward -X, tail toward +X.
                float2 q=float2((p.z-_Centre.z)/max(_HalfExtent.z,.01),-(p.x-_Centre.x)/max(_HalfExtent.x,.01));
                float wing=saturate((abs(q.x)-.16)/.84);
                float wave=_SwimTime*.66+q.y*1.15;
                // Broad wing strokes travel toward the tips; the spine stays quiet.
                p.y+=sin(wave)*wing*wing*.10;
                float dyz=sin(wave)*.20*wing*sign(q.x)/(.84*max(_HalfExtent.z,.01));
                float dyx=-cos(wave)*wing*wing*.115/max(_HalfExtent.x,.01);
                float tail=saturate((-q.y-.35)/.65);
                p.x+=tail*tail*.17;
                p.z+=sin(_SwimTime*.74+q.y*3.1)*tail*tail*.018;
                float3 n=normalize(float3(i.normalOS.x-dyx*i.normalOS.y,i.normalOS.y,i.normalOS.z-dyz*i.normalOS.y));
                o.positionWS=TransformObjectToWorld(p); o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(n);o.uv=TRANSFORM_TEX(i.uv,_BaseMap);o.shape=q;
                return o;
            }
            half4 frag(Varyings i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 skin=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb;
                half3 n=normalize(i.normalWS),v=normalize(GetWorldSpaceViewDir(i.positionWS));
                Light sun=GetMainLight();
                half belly=saturate(-n.y*.75+.3);
                half3 tint=lerp(half3(.035,.17,.24),half3(.29,.56,.62),belly);
                tint=lerp(tint,lerp(half3(.012,.06,.11),half3(.04,.13,.20),belly),_SanctuaryTone);
                half luminance=dot(skin,half3(.2126,.7152,.0722));
                half3 body=tint*(.42+luminance*.94);
                half light=.55+saturate(dot(n,sun.direction))*.45;
                half rim=pow(1-abs(dot(n,v)),3);
                float x=abs(i.shape.x),z=i.shape.y;
                float veinA=exp(-abs(z-.22+.33*x*x)*105);
                float veinB=exp(-abs(z+.19-.16*x)*100);
                float veins=(veinA+veinB*.55)*smoothstep(.18,.50,x);
                half pulse=.75+.25*sin(_SwimTime*.4-x*2.4);
                half3 glow=half3(.25,.65,.73)*rim*.48+half3(.53,.68,.44)*veins*pulse*.55;
                glow*=lerp(1.0,.62,_SanctuaryTone);
                half3 lit=body*light+glow;
                // A distinct distant silhouette with water haze; ordinary reef fog would erase it completely.
                float distance=length(GetWorldSpaceViewDir(i.positionWS));
                float haze=saturate((distance-18)/120)*.5;
                half3 water=lerp(half3(.10,.35,.41),half3(.035,.22,.34),_SanctuaryTone);
                return half4(lerp(lit,water,haze),1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
