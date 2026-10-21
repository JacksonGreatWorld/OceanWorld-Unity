Shader "SwanUnderwater/Seagrass"
{
    Properties
    {
        _RootColor ("Shaded root", Color) = (0.055, 0.067, 0.062, 1)
        _TipColor ("Sunlit blade", Color) = (0.23, 0.265, 0.24, 1)
        _SwayStrength ("Current bend / leaf height", Range(0, 0.3)) = 0.18
        _SwaySpeed ("Current speed", Range(0, 2)) = 0.8
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "SeagrassForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RootColor;
                half4 _TipColor;
                float _SwayStrength;
                float _SwaySpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 motion : TEXCOORD1; // Full leaf height in metres, individual phase.
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half3 tint : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                float heightFraction = input.uv.y;
                float wave = sin(dot(world.xz, float2(0.27, 0.18)) + _Time.y * _SwaySpeed);
                float flutter = sin(_Time.y * (_SwaySpeed * 1.65) + input.motion.y) * 0.18;
                float2 bend = float2(0.92, 0.38) * (wave + flutter) * _SwayStrength;
                world.xz += bend * (input.motion.x * heightFraction * heightFraction);
                half3 normal = TransformObjectToWorldNormal(input.normalOS);
                // First-order normal correction for the anchored quadratic bend.
                normal.y -= dot(normal.xz, bend) * (2.0 * heightFraction);
                output.normalWS = normalize(normal);
                output.positionWS = world;
                output.positionCS = TransformWorldToHClip(world);
                output.uv = input.uv;
                output.tint = input.color.rgb;
                return output;
            }

            half4 frag(Varyings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 normal = normalize(input.normalWS) * IS_FRONT_VFACE(frontFace, 1.0h, -1.0h);
                half height = saturate(input.uv.y);
                half3 albedo = lerp(_RootColor.rgb, _TipColor.rgb, smoothstep(0.05h, 0.95h, height)) * input.tint;
                Light light = GetMainLight();
                half lambert = saturate(dot(normal, light.direction) * 0.65h + 0.35h);
                half3 ambient = max(SampleSH(normal), half3(0.11h, 0.16h, 0.16h));
                // A little diffuse transmission gives thin leaf tips colour without alpha blending.
                half transmission = saturate(dot(-normal, light.direction)) * height * 0.12h;
                half3 lit = albedo * (ambient + light.color * (lambert * 0.8h + transmission));
                // URP 17 computes fog from each eye's view depth; use the same RenderSettings fog as the seabed.
                float fog = InitializeInputDataFog(float4(input.positionWS, 1.0), 0.0);
                return half4(MixFog(lit, fog), 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
