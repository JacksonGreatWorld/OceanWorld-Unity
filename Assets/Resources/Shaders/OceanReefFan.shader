Shader "SwanUnderwater/OceanReefFan"
{
    Properties
    {
        _BaseMap ("Original coral colour and opacity", 2D) = "white" {}
        _BaseColor ("Original colour factor", Color) = (1,1,1,1)
        [Normal] _BumpMap ("Tangent-space normal", 2D) = "bump" {}
        _BumpScale ("Normal strength", Range(0,2)) = 1
        [Enum(glTF_RGB,0,Unity_imported_normal,1)] _NormalEncoding ("Normal texture encoding", Float) = 0
        _MaskMap ("Occlusion R / roughness G / metallic B", 2D) = "white" {}
        _OcclusionMap ("Optional independent occlusion R", 2D) = "white" {}
        [Toggle] _UseOcclusionMap ("Use independent source occlusion", Float) = 0
        _OcclusionStrength ("Occlusion strength", Range(0,1)) = 1
        _Roughness ("Roughness factor", Range(0,1)) = 1
        _Metallic ("Metallic factor", Range(0,1)) = 0
        _EmissionMap ("Original emission", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission factor", Color) = (0,0,0,1)
        [Toggle] _AlphaClip ("Clip original texture opacity", Float) = 1
        _Cutoff ("Opacity cutoff", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Culling", Float) = 0
        _SwayAmplitude ("Maximum current displacement in world metres", Range(0,0.15)) = 0.025
        _SwayFrequency ("Slow current angular frequency", Range(0,2)) = 0.65
        _SwayHeight ("Root-to-tip height in mesh units", Float) = 1
        _SwayRootHeight ("Root height in mesh units", Float) = 0
        [Toggle] _SwayUseUV2 ("Use authored UV2.x weights (root 0 / tip 1)", Float) = 0
        _SwayPhase ("Additional individual phase in radians", Float) = 0
        _CurrentDirection ("World-space current direction (XZ)", Vector) = (0.84,0,0.54,0)
        [HideInInspector] _ValidationTime ("Editor capture time", Float) = 0
        [HideInInspector] _ValidationTimeEnabled ("Editor capture override", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BumpMap_ST;
            float4 _MaskMap_ST;
            float4 _OcclusionMap_ST;
            float4 _EmissionMap_ST;
            half4 _BaseColor;
            half4 _EmissionColor;
            float4 _CurrentDirection;
            float _BumpScale;
            float _NormalEncoding;
            float _OcclusionStrength;
            float _UseOcclusionMap;
            float _Roughness;
            float _Metallic;
            float _AlphaClip;
            float _Cutoff;
            float _Cull;
            float _SwayAmplitude;
            float _SwayFrequency;
            float _SwayHeight;
            float _SwayRootHeight;
            float _SwayUseUV2;
            float _SwayPhase;
            float _ValidationTime;
            float _ValidationTimeEnabled;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);
        TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            // Opt-in only: x = anchored weight, y = a per-colony phase in radians.
            // Texture/lightmap UV2 from an arbitrary download is not a motion contract.
            float2 swayData : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        // Shared by the colour, shadow and optional depth prepass. No transform or
        // camera state is changed, and displacement stays in world metres at any scale.
        void ReefFanWorld(Attributes input, out float3 positionWS, out half3 normalWS,
            out half3 tangentWS)
        {
            positionWS=TransformObjectToWorld(input.positionOS.xyz);
            normalWS=TransformObjectToWorldNormal(input.normalOS);
            tangentWS=TransformObjectToWorldDir(input.tangentOS.xyz);
            float inverseHeight=rcp(max(abs(_SwayHeight),0.0001));
            float heightWeight=saturate((input.positionOS.y-_SwayRootHeight)*inverseHeight);
            float useAuthored=step(0.5,_SwayUseUV2);
            float weight=lerp(heightWeight,saturate(input.swayData.x),useAuthored);
            // Quadratic anchoring fixes both root position and its first derivative.
            float curve=weight*weight;
            float time=lerp(_Time.y,_ValidationTime,saturate(_ValidationTimeEnabled));
            float3 rootWS=TransformObjectToWorld(float3(0,_SwayRootHeight,0));
            float phase=dot(rootWS.xz,float2(0.17,0.105))+_SwayPhase+
                input.swayData.y*useAuthored;
            float t=time*clamp(_SwayFrequency,0,2);
            float2 stream=_CurrentDirection.xz;
            stream=dot(stream,stream)>0.0001?normalize(stream):float2(0.84,0.543);
            float2 across=float2(-stream.y,stream.x);
            float surge=0.68*sin(phase-t)+0.22*sin(phase*0.57-t*0.43+1.7);
            float eddy=0.14*sin(phase*1.83-t*1.31+0.8);
            float3 objectUp=TransformObjectToWorldDir(float3(0,1,0),false);
            float scaleY=max(length(objectUp),0.0001);
            // Very small admitted colonies must not sway farther than their size.
            float amplitude=min(clamp(_SwayAmplitude,0,0.15),abs(_SwayHeight)*scaleY*0.12);
            float2 current=(stream*surge+across*eddy)*amplitude;
            float3 displacement=float3(current.x,0,current.y);
            positionWS+=displacement*curve;

            // The height fallback has an analytic shear normal/tangent correction.
            // Arbitrary authored weights have no implied spatial derivative; their
            // source normals remain stable under this deliberately small deformation.
            float3 up=objectUp/scaleY;
            float derivative=2*weight*inverseHeight/scaleY*(1-useAuthored);
            float denominator=max(0.25,1+derivative*dot(up,displacement));
            normalWS=normalize(normalWS-up*(derivative*dot(normalWS,displacement)/denominator));
            tangentWS=normalize(tangentWS+displacement*(derivative*dot(up,tangentWS)));
        }

        half4 ReefFanBase(float2 uv)
        {
            // glTF/imported UV orientation is retained. No extra Y flip belongs here.
            return SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,TRANSFORM_TEX(uv,_BaseMap))*_BaseColor;
        }
        void ReefFanClip(half alpha)
        {
            if(_AlphaClip>0.5)clip(alpha-saturate(_Cutoff));
        }
        ENDHLSL

        Pass
        {
            Name "ReefFanForward"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            ZWrite On
            ZTest LEqual
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
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings vert(Attributes input)
            {
                Varyings output=(Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                ReefFanWorld(input,output.positionWS,output.normalWS,output.tangentWS.xyz);
                output.positionCS=TransformWorldToHClip(output.positionWS);
                output.tangentWS.w=input.tangentOS.w*GetOddNegativeScale();
                output.uv=input.uv;
                return output;
            }
            half4 frag(Varyings input, FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 base=ReefFanBase(input.uv);
                ReefFanClip(base.a);
                half3 normal=normalize(input.normalWS);
                half3 tangent=normalize(input.tangentWS.xyz-normal*dot(normal,input.tangentWS.xyz));
                half3 bitangent=cross(normal,tangent)*input.tangentWS.w;
                half4 packedNormal=SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,
                    TRANSFORM_TEX(input.uv,_BumpMap));
                // The isolated GLB baker preserves glTF RGB payloads, while a Unity
                // NormalMap importer may swizzle to RG/AG. Do not guess from platform.
                half3 detail;
                if(_NormalEncoding<0.5)
                {
                    detail=packedNormal.xyz*2.0h-1.0h;
                    detail.xy*=_BumpScale;
                }
                else detail=UnpackNormalScale(packedNormal,_BumpScale);
                normal=normalize(tangent*detail.x+bitangent*detail.y+normal*detail.z);
                normal*=IS_FRONT_VFACE(face,1.0h,-1.0h);
                half3 mask=SAMPLE_TEXTURE2D(_MaskMap,sampler_MaskMap,TRANSFORM_TEX(input.uv,_MaskMap)).rgb;
                InputData lighting=(InputData)0;
                lighting.positionWS=input.positionWS;
                lighting.positionCS=input.positionCS;
                lighting.normalWS=normal;
                lighting.viewDirectionWS=GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    lighting.shadowCoord=ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    lighting.shadowCoord=TransformWorldToShadowCoord(input.positionWS);
                #endif
                half hemisphere=saturate(normal.y*0.5h+0.5h);
                half3 waterBounce=lerp(half3(0.025h,0.055h,0.065h),half3(0.075h,0.105h,0.105h),hemisphere);
                lighting.bakedGI=max(SampleSH(normal),0)*0.78h+waterBounce;
                lighting.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask=half4(1,1,1,1);
                SurfaceData surface=(SurfaceData)0;
                surface.albedo=base.rgb;
                surface.metallic=saturate(mask.b*_Metallic);
                surface.smoothness=1.0h-saturate(mask.g*_Roughness);
                half occlusion=mask.r;
                if(_UseOcclusionMap>0.5)occlusion=SAMPLE_TEXTURE2D(_OcclusionMap,sampler_OcclusionMap,
                    TRANSFORM_TEX(input.uv,_OcclusionMap)).r;
                surface.occlusion=lerp(1.0h,occlusion,saturate(_OcclusionStrength));
                surface.normalTS=half3(0,0,1);
                surface.alpha=1;
                surface.emission=SAMPLE_TEXTURE2D(_EmissionMap,sampler_EmissionMap,
                    TRANSFORM_TEX(input.uv,_EmissionMap)).rgb*_EmissionColor.rgb;
                half3 lit=UniversalFragmentPBR(lighting,surface).rgb;
                float extraDepth=max(0.0,-input.positionWS.y-4.3);
                lit*=exp2(-extraDepth*half3(0.065h,0.026h,0.010h));
                float fog=InitializeInputDataFog(float4(input.positionWS,1),0);
                return half4(MixFog(lit,fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ReefFanShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            ShadowVaryings shadowVert(Attributes input)
            {
                ShadowVaryings output=(ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 world;half3 normal,tangent;
                ReefFanWorld(input,world,normal,tangent);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDir=normalize(_LightPosition-world);
                #else
                    float3 lightDir=_LightDirection;
                #endif
                // Use the light-facing side of a two-sided card for shadow bias.
                if(_Cull<0.5)normal*=dot(normal,lightDir)<0?-1:1;
                output.positionCS=TransformWorldToHClip(ApplyShadowBias(world,normal,lightDir));
                #if UNITY_REVERSED_Z
                    output.positionCS.z=min(output.positionCS.z,UNITY_NEAR_CLIP_VALUE*output.positionCS.w);
                #else
                    output.positionCS.z=max(output.positionCS.z,UNITY_NEAR_CLIP_VALUE*output.positionCS.w);
                #endif
                output.uv=input.uv;
                return output;
            }
            half4 shadowFrag(ShadowVaryings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ReefFanClip(ReefFanBase(input.uv).a);
                return 0;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ReefFanDepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #pragma multi_compile_instancing
            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            DepthVaryings depthVert(Attributes input)
            {
                DepthVaryings output=(DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 world;half3 normal,tangent;
                ReefFanWorld(input,world,normal,tangent);
                output.positionCS=TransformWorldToHClip(world);
                output.uv=input.uv;
                return output;
            }
            half4 depthFrag(DepthVaryings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ReefFanClip(ReefFanBase(input.uv).a);
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
