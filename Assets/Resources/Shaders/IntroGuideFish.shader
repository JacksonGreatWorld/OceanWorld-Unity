Shader "SwanUnderwater/IntroGuideFish"
{
    Properties
    {
        _BaseMap ("Original fish albedo", 2D) = "white" {}
        _BaseColor ("Original fish tint", Color) = (1,1,1,1)
        _MetallicRoughnessMap ("glTF metallic B / roughness G", 2D) = "white" {}
        _Metallic ("Metallic factor", Range(0,1)) = 0
        _Roughness ("Roughness factor", Range(0,1)) = 1
        _SwingAmp ("Reef clownfish swing amplitude", Float) = 0.065
        _SwingFreq ("Reef clownfish cadence (CPU phase rad/s)", Float) = 4.5
        _FishHalfLength ("Reef clownfish half length (mesh space)", Float) = 0.502
        _VerticalBob ("Reef clownfish vertical bob", Float) = 0.008
        _FishBody ("Species style and measured mesh half extents", Vector) = (0,.24,.28,.502)
        _FishFins ("Tail start, fin stroke, fin cadence, spine flexibility", Vector) = (.05,.018,2.4,.1)
        _SwimMotion ("Continuous phase, stroke, turn, enabled", Vector) = (0,1,0,1)
        _SwimPhase ("Continuous reef swim phase", Float) = 0
        _Glow ("Soft VST illumination", Range(0,2)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "GuideFishForward"
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "OceanFishDeformation.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_MetallicRoughnessMap); SAMPLER(sampler_MetallicRoughnessMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _MetallicRoughnessMap_ST;
                half4 _BaseColor;
                half _Metallic;
                half _Roughness;
                float _SwingAmp;
                float _SwingFreq;
                float _FishHalfLength;
                float _VerticalBob;
                float _SwimPhase;
                float4 _FishBody;
                float4 _FishFins;
                float4 _SwimMotion;
                half _Glow;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // The same mesh-space tail/fin function is used in the underwater world.
                float3 p, deformedNormal, deformedTangent;
                OceanDeformFish(i.positionOS.xyz, i.normalOS, float3(1, 0, 0),
                    _SwimPhase, _SwimMotion.y, _SwimMotion.z, _FishBody, _FishFins,
                    _SwingAmp, _VerticalBob, p, deformedNormal, deformedTangent);
                VertexPositionInputs positions = GetVertexPositionInputs(p);
                o.positionCS = positions.positionCS;
                o.positionWS = positions.positionWS;
                o.normalWS = TransformObjectToWorldNormal(normalize(deformedNormal));
                o.uv = i.uv;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, TRANSFORM_TEX(i.uv, _BaseMap)).rgb * _BaseColor.rgb;
                half4 packed = SAMPLE_TEXTURE2D(_MetallicRoughnessMap, sampler_MetallicRoughnessMap,
                    TRANSFORM_TEX(i.uv, _MetallicRoughnessMap));
                InputData lighting = (InputData)0;
                lighting.positionWS = i.positionWS;
                lighting.normalWS = normalize(i.normalWS);
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                lighting.bakedGI = max(SampleSH(lighting.normalWS), half3(0.20, 0.22, 0.24));
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                lighting.shadowMask = half4(1,1,1,1);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = saturate(packed.b * _Metallic);
                surface.smoothness = 1.0h - saturate(packed.g * _Roughness);
                surface.normalTS = half3(0,0,1);
                surface.occlusion = 1;
                surface.alpha = 1;
                half rim = 1.0h - saturate(dot(lighting.normalWS, lighting.viewDirectionWS));
                rim *= rim;
                // Preserve the coloured skin/black markings in VST without a light or glow shell.
                surface.emission = albedo * (0.28h + 0.45h * _Glow) + half3(0.025, 0.095, 0.13) * rim * _Glow;
                half4 color = UniversalFragmentPBR(lighting, surface);
                float fog = InitializeInputDataFog(float4(i.positionWS, 1.0), 0.0);
                color.rgb = MixFog(color.rgb, fog);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
