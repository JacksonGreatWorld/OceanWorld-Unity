// Licensed static butterfly source: canonical +Z nose, procedural swim, no rig claim.
// Original 1K albedo/opacity, RG tangent normals and metallic/roughness are retained.
// Alpha-to-coverage keeps authored translucent fins without transparent sorting.
// Near motion uses the same instanced _SwimMotion contract as OceanFishMotion.

Shader "SwanUnderwater/ButterflyFishSwim"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Authored fin minimum coverage", Range(0,1)) = 0.20
        _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1.0
        _MetallicGlossMap ("Metallic-Roughness (B=metallic, G=roughness)", 2D) = "black" {}
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        _OcclusionStrength ("Occlusion Strength", Range(0,1)) = 1.0
        _EmissionMap ("Emission", 2D) = "white" {}
        _EmissionColor ("Emission Color", Color) = (0,0,0)

        _SwingAmp ("Swing Amplitude", Float) = 0.065
        _SwingFreq ("Swing Frequency", Float) = 4.5
        _SwingPhase ("Swing Phase", Float) = 0.0
        _FishHalfLength ("Fish Half Length (Z)", Float) = 0.502
        _VerticalBob ("Vertical Bob Amount", Float) = 0.008
        _FishBody ("Species style and measured mesh half extents", Vector) = (0,.24,.28,.502)
        _FishFins ("Tail start, fin stroke, fin cadence, spine flexibility", Vector) = (.05,.018,2.4,.1)
        _SwimMotion ("Optional continuous phase, stroke, turn, enabled", Vector) = (0,1,0,0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
        }

        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            AlphaToMask On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _NORMALMAP
            #pragma multi_compile_instancing
            // Materials are created at runtime, so keep both local variants in player builds.
            #pragma multi_compile_local _ _SCHOOL_SIMPLE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "OceanFishDeformation.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                #if !defined(_SCHOOL_SIMPLE)
                float4 tangentOS  : TANGENT;
                #endif
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                #if !defined(_SCHOOL_SIMPLE)
                float3 tangentWS   : TEXCOORD2;
                float3 bitangentWS : TEXCOORD3;
                #endif
                float3 positionWS  : TEXCOORD4;
                float fogFactor   : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                // Match URP's instance-then-stereo signature layout. Metal's
                // stereo instancing compiler rejects the reverse packed order.
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            #if !defined(_SCHOOL_SIMPLE)
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            #endif
            TEXTURE2D(_MetallicGlossMap);
            SAMPLER(sampler_MetallicGlossMap);
            #if !defined(_SCHOOL_SIMPLE)
            TEXTURE2D(_OcclusionMap);
            SAMPLER(sampler_OcclusionMap);
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);
            #endif

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _BumpScale;
                float _Metallic;
                float _Smoothness;
                float _OcclusionStrength;
                float4 _EmissionColor;
                float _SwingAmp;
                float _SwingFreq;
                float _FishHalfLength;
                float _VerticalBob;
                float _Cutoff;
                float4 _FishBody;
                float4 _FishFins;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
                UNITY_DEFINE_INSTANCED_PROP(float, _SwingPhase)
                UNITY_DEFINE_INSTANCED_PROP(float4, _SwimMotion)
            UNITY_INSTANCING_BUFFER_END(Props)

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float swingPhase = UNITY_ACCESS_INSTANCED_PROP(Props, _SwingPhase);

                float4 motion = UNITY_ACCESS_INSTANCED_PROP(Props, _SwimMotion);
                float useClock = step(.5, motion.w);
                float phase = lerp(_Time.y * _SwingFreq + swingPhase, motion.x, useClock);
                float stroke = lerp(1.0, motion.y, useClock);
                float3 sourceTangent = float3(1, 0, 0);
                #if !defined(_SCHOOL_SIMPLE)
                sourceTangent = input.tangentOS.xyz;
                #endif
                float3 displacedOS, normalOS, tangentOS;
                OceanDeformFish(input.positionOS.xyz, input.normalOS, sourceTangent,
                    phase, stroke, motion.z * useClock, _FishBody, _FishFins,
                    _SwingAmp, _VerticalBob, displacedOS, normalOS, tangentOS);

                output.positionHCS = TransformObjectToHClip(displacedOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = TransformObjectToWorldNormal(normalize(normalOS));
                #if !defined(_SCHOOL_SIMPLE)
                output.tangentWS = normalize(TransformObjectToWorldDir(tangentOS));
                output.bitangentWS = cross(output.normalWS, output.tangentWS) * input.tangentOS.w * unity_WorldTransformParams.w;
                #endif
                output.positionWS = TransformObjectToWorld(displacedOS);
                output.fogFactor = ComputeFogFactor(output.positionHCS.z);
                return output;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float4 baseColor = UNITY_ACCESS_INSTANCED_PROP(Props, _BaseColor);
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * baseColor;
                clip(albedo.a - _Cutoff);

                // Normal mapping
                half3 N = normalize(i.normalWS);
                #if !defined(_SCHOOL_SIMPLE)
                half4 packedNormal = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv);
                // Native ASTC retains source R/G directly: no platform-dependent AG swizzle.
                half2 normalXY = packedNormal.rg * 2.0h - 1.0h;
                half3 tangentNormal = half3(normalXY, sqrt(saturate(1.0h - dot(normalXY, normalXY))));
                tangentNormal.xy *= _BumpScale;
                N = normalize(mul(tangentNormal, half3x3(i.tangentWS, i.bitangentWS, i.normalWS)));
                #endif

                // Thin authored fins remain lit on their back face with the correct facing normal.
                N *= IS_FRONT_VFACE(facing, 1.0h, -1.0h);

                // Main directional light
                Light mainLight = GetMainLight();
                half NdotL = saturate(dot(N, mainLight.direction));

                // URP spherical-harmonics ambient, boosted for underwater.
                // A hemispheric fill keeps down-facing / side-facing fish readable
                // instead of going nearly black in the dim underwater ambient.
                half3 ambient = SampleSH(N) + half3(0.10, 0.16, 0.18) * (0.5 + 0.5 * saturate(N.y));
                ambient = max(ambient, half3(0.09, 0.13, 0.15));

                // Metallic / roughness from glTF-packed texture (B=metallic, G=roughness)
                // SchoolFish contains this map: keep its actual channels and the existing black
                // shader fallback for a missing map, rather than inventing metallic/roughness values.
                half4 metallicGloss = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, i.uv);
                half metallic = metallicGloss.b * _Metallic;
                half smoothness = (1.0 - metallicGloss.g) * _Smoothness;

                // Simple PBR: diffuse + specular
                half3 diffColor = albedo.rgb * (1.0 - metallic);
                half3 specColor = lerp(half3(0.04, 0.04, 0.04), albedo.rgb, metallic);

                // Schlick fresnel
                half3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                half3 H = SafeNormalize(mainLight.direction + V);
                half NdotH = saturate(dot(N, H));
                half fresnel = pow(1.0 - saturate(dot(H, V)), 5.0);
                half3 reflectance = specColor + (1.0h - specColor) * fresnel;
                // A compact highlight preserves the real painted skin. No extra texture
                // sampling or synthetic detail is added to the existing 1K source maps.
                half3 specular = reflectance * pow(NdotH, lerp(8.0h, 72.0h, smoothness * smoothness)) * NdotL;
                half3 diffuse = diffColor * NdotL;

                // Occlusion
                #if defined(_SCHOOL_SIMPLE)
                half occlusion = 1.0h;
                #else
                half occlusion = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, i.uv).r;
                occlusion = lerp(1.0, occlusion, _OcclusionStrength);
                #endif

                half3 lit = (diffuse * mainLight.color + specular * mainLight.color) * occlusion + albedo.rgb * ambient * occlusion;

                // Emission
                #if !defined(_SCHOOL_SIMPLE)
                half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb;
                lit += emission;
                #endif

                // Underwater fog
                lit = MixFog(lit, i.fogFactor);

                return half4(lit, albedo.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
