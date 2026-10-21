Shader "SwanUnderwater/SanctuaryOrganic"
{
    Properties
    {
        _BaseMap ("Reef colour", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _BumpMap ("Reef normal", 2D) = "bump" {}
        _BumpScale ("Normal strength", Range(0,2)) = 0.65
        _Sway ("Living coral current", Range(0,0.08)) = 0
        _WorldMapping ("World-space limestone", Range(0,1)) = 0
        _WorldScale ("Limestone repeats per metre", Range(0.1,3)) = 0.85
        _GrowthPigment ("Living surface pigmentation", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float _BumpScale;
            float _Sway;
            half _WorldMapping;
            float _WorldScale;
            half _GrowthPigment;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        float3 SanctuaryPosition(float3 p, half anchor)
        {
            // Mesh colour alpha stores the anchored current weight; geometry remains opaque.
            float wave = sin(_Time.y * 0.75 + p.x * 1.3 + p.z * 0.8);
            return p + float3(wave, 0, wave * 0.37) * (_Sway * anchor);
        }
        ENDHLSL
        Pass
        {
            Name "SanctuaryForward"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                half3 color : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings vert(Attributes input)
            {
                Varyings output=(Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS=TransformObjectToWorld(SanctuaryPosition(input.positionOS.xyz,input.color.a));
                output.positionCS=TransformWorldToHClip(output.positionWS);
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS=half4(TransformObjectToWorldDir(input.tangentOS.xyz),input.tangentOS.w*GetOddNegativeScale());
                output.uv=TRANSFORM_TEX(input.uv,_BaseMap);
                output.color=input.color.rgb;
                return output;
            }
            half4 frag(Varyings input, FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 geometricNormal=normalize(input.normalWS);
                half3 normal=geometricNormal;
                half3 colour;
                // A material-uniform branch keeps living coral at two texture reads.
                // Limestone alone uses three projections, so vertical arch faces retain
                // the same physical texel scale as horizontal ledges and never stretch.
                if (_WorldMapping > 0.5h)
                {
                    half3 weights=abs(geometricNormal);
                    weights*=weights;
                    weights*=weights;
                    weights/=max(dot(weights,half3(1,1,1)),0.0001h);
                    half3 axisSign=half3(geometricNormal.x < 0 ? -1 : 1,
                        geometricNormal.y < 0 ? -1 : 1,geometricNormal.z < 0 ? -1 : 1);
                    float3 p=input.positionWS*_WorldScale;
                    float2 uvX=float2(-p.z*axisSign.x,p.y);
                    float2 uvY=float2(p.x,-p.z*axisSign.y);
                    float2 uvZ=float2(p.x*axisSign.z,p.y);
                    colour=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uvX).rgb*weights.x
                        + SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uvY).rgb*weights.y
                        + SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uvZ).rgb*weights.z;
                    half3 nx=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,uvX),_BumpScale);
                    half3 ny=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,uvY),_BumpScale);
                    half3 nz=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,uvZ),_BumpScale);
                    half3 perturb=half3(0,nx.y,-nx.x*axisSign.x)*weights.x
                        + half3(ny.x,0,-ny.y*axisSign.y)*weights.y
                        + half3(nz.x*axisSign.z,nz.y,0)*weights.z;
                    // Perturb the actual mesh normal rather than substituting each
                    // projection's axis normal, preserving softly eroded silhouettes.
                    normal=normalize(geometricNormal+perturb);
                    half grey=dot(colour,half3(.2126h,.7152h,.0722h));
                    colour=lerp(grey.xxx,colour,.44h);
                    // Broad mineral variation follows the rock in world space. It is
                    // deliberately subtle; crevice darkness comes from mesh colour and
                    // real shadows, rather than a painted horizontal contour band.
                    half minerals=sin(p.x*.71+p.y*.93+p.z*.37)*sin(p.z*.53-p.y*.61);
                    colour*=lerp(half3(.88h,.94h,.95h),half3(1.05h,1.01h,.91h),minerals*.5h+.5h);
                }
                else
                {
                    half3 tangent=normalize(input.tangentWS.xyz);
                    half3 bitangent=cross(normal,tangent)*input.tangentWS.w;
                    half3 detail=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,input.uv),_BumpScale);
                    normal=normalize(tangent*detail.x+bitangent*detail.y+normal*detail.z);
                    colour=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb;
                    // Scanned detail supplies the polyp texture; living vertex pigments
                    // supply warm colony colours without multiplying them into dark brown.
                    half microshade=.48h+.52h*saturate(dot(colour,half3(.2126h,.7152h,.0722h))*2.8h);
                    colour=lerp(colour,microshade.xxx,_GrowthPigment);
                }
                normal*=IS_FRONT_VFACE(face,1.0h,-1.0h);
                half3 albedo=colour*_BaseColor.rgb*input.color;
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord=ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    float4 shadowCoord=TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light sun=GetMainLight(shadowCoord,input.positionWS,half4(1,1,1,1));
                half diffuse=saturate(dot(normal,sun.direction));
                half hemisphere=saturate(normal.y*.5h+.5h);
                half3 waterBounce=lerp(half3(.025h,.055h,.065h),half3(.075h,.105h,.105h),hemisphere);
                half3 ambient=max(SampleSH(normal),0)*.78h+waterBounce;
                half direct=diffuse*sun.shadowAttenuation*sun.distanceAttenuation;
                half3 lit=albedo*(ambient+sun.color*(direct*.94h));
                half extraDepth=max(0.0h,-input.positionWS.y-4.3h);
                lit*=exp2(-extraDepth*half3(.065h,.026h,.010h));
                float opticalDepth=distance(input.positionWS,GetCameraPositionWS())*unity_FogParams.x;
                float transmission=saturate(exp2(-opticalDepth*opticalDepth));
                return half4(lerp(unity_FogColor.rgb,lit,transmission),1.0h);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing
            float3 _LightDirection;
            float3 _LightPosition;
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float4 shadowVert(Attributes input):SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 world=TransformObjectToWorld(SanctuaryPosition(input.positionOS.xyz,input.color.a));
                float3 normal=TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDir=normalize(_LightPosition-world);
                #else
                    float3 lightDir=_LightDirection;
                #endif
                float4 clip=TransformWorldToHClip(ApplyShadowBias(world,normal,lightDir));
                #if UNITY_REVERSED_Z
                    clip.z=min(clip.z,UNITY_NEAR_CLIP_VALUE*clip.w);
                #else
                    clip.z=max(clip.z,UNITY_NEAR_CLIP_VALUE*clip.w);
                #endif
                return clip;
            }
            half4 shadowFrag():SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
