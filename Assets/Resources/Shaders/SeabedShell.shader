Shader "SwanUnderwater/SeabedShell"
{
    Properties { _DetailStrength ("Worn growth lines", Range(0, 1)) = 0.7 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "ShellForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            // URP 17 selects static quality keywords on Android XR.
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _DetailStrength;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 shape : TEXCOORD1;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float2 shape : TEXCOORD3;
                half3 color : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv; output.shape = input.shape; output.color = input.color.rgb;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float radius = input.shape.x;
                float fan = 1.0 - step(0.5, input.shape.y);
                float clam = step(0.5, input.shape.y) * (1.0 - step(1.5, input.shape.y));
                float2 ribUV = input.uv + float2(0, fan * 0.3486);
                float angle = atan2(ribUV.x, ribUV.y + 0.00001);
                float ribPhase = angle * lerp(10.0, 23.271, fan);
                float growthPhase = radius * 57.0 + sin(angle * 3.0) * 0.6;
                float ribFilter = saturate(1.0 - fwidth(ribPhase) * 0.3);
                float growthFilter = saturate(1.0 - fwidth(growthPhase) * 0.3);
                float rib = cos(ribPhase) * ribFilter * (1.0 - clam * 0.8) * smoothstep(0.1, 0.4, radius);
                float growth = sin(growthPhase) * growthFilter;
                half3 albedo = input.color * (1.0 + _DetailStrength * (rib * 0.055 + growth * 0.045));
                // Tiny procedural relief, filtered away when a shell occupies only a few pixels.
                float relief = _DetailStrength * (rib * 0.00017 + growth * 0.000035);
                float3 normal = normalize(input.normalWS);
                float3 dpdx = ddx(input.positionWS), dpdy = ddy(input.positionWS);
                float3 r1 = cross(dpdy, normal), r2 = cross(normal, dpdx);
                float determinant = dot(dpdx, r1);
                float3 gradient = (ddx(relief) * r1 + ddy(relief) * r2) *
                    (sign(determinant) / max(abs(determinant), 0.0000001));
                normal = normalize(normal - gradient);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light sun = GetMainLight(shadowCoord, input.positionWS, half4(1,1,1,1));
                half3 ambient = max(SampleSH(normal), half3(0.07h, 0.10h, 0.10h));
                half diffuse = saturate(dot(normal, sun.direction));
                half3 halfVector = SafeNormalize(sun.direction + GetWorldSpaceNormalizeViewDir(input.positionWS));
                half specular = pow(saturate(dot(normal, halfVector)), 12.0h) * 0.015h;
                half3 lit = albedo * ambient + (albedo * diffuse + specular) * sun.color *
                    sun.distanceAttenuation * sun.shadowAttenuation;
                float fog = InitializeInputDataFog(float4(input.positionWS, 1.0), 0.0);
                return half4(MixFog(lit, fog), 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
