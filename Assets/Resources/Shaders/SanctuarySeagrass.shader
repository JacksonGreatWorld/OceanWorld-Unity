Shader "SwanUnderwater/SanctuarySeagrass"
{
    Properties
    {
        _RootColor ("Shaded olive root", Color) = (0.125, 0.15, 0.086, 1)
        _TipColor ("Olive teal leaf tip", Color) = (0.34, 0.405, 0.26, 1)
        _SwayStrength ("Current bend / leaf height", Range(0, 0.75)) = 0.67
        _SwaySpeed ("Slow current speed", Range(0, 2)) = 0.64
        [HideInInspector] _ValidationTime ("Editor capture time", Float) = 0
        [HideInInspector] _ValidationTimeEnabled ("Editor capture override", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "SeagrassCurrentForward"
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RootColor;
                half4 _TipColor;
                float _SwayStrength;
                float _SwaySpeed;
                float _ValidationTime;
                float _ValidationTimeEnabled;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 motion : TEXCOORD1; // Full leaf height and small individual phase.
                float2 anchor : TEXCOORD2; // One stable world-space root for the whole blade.
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
                // The override is written only by the UNITY_EDITOR validation hook;
                // the Player uses real shader time without any CPU animation driver.
                float time = lerp(_Time.y, _ValidationTime, saturate(_ValidationTimeEnabled)) * _SwaySpeed;
                float h = input.uv.y;
                float phase = dot(input.anchor, float2(.17, .105));
                // A consistent slow surge travels across the habitat. Adjacent roots
                // share it, while leaf-tip lag and small eddies prevent rigid synchrony.
                float surge = .20 + .43 * sin(phase - time) + .13 * sin(phase * .57 - time * .43 + 1.7);
                float eddy = .12 * sin(phase * 1.83 - time * 1.65 + input.motion.y - h * .75);
                float2 stream = float2(.84, .543);
                float2 across = float2(-stream.y, stream.x);
                float2 bend = (stream * surge + across * eddy) * _SwayStrength;
                float anchoredCurve = h * h * (1.25 - .25 * h);
                world.xz += input.motion.x * anchoredCurve * bend;
                world.y -= input.motion.x * (.20 * dot(bend, bend)) * h * h * h;
                half3 normal = TransformObjectToWorldNormal(input.normalOS);
                normal.y -= dot(normal.xz, bend) * (2.5 * h - .75 * h * h);
                output.positionWS = world;
                output.positionCS = TransformWorldToHClip(world);
                output.normalWS = normalize(normal);
                output.uv = input.uv;
                output.tint = input.color.rgb;
                return output;
            }
            half4 frag(Varyings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 normal = normalize(input.normalWS) * IS_FRONT_VFACE(frontFace, 1.0h, -1.0h);
                half h = saturate(input.uv.y);
                half3 albedo = lerp(_RootColor.rgb, _TipColor.rgb, smoothstep(.04h, .92h, h)) * input.tint;
                // A broad central leaf vein adds readable close detail without an
                // alpha texture, a transparent fringe or another material/pass.
                half rib = saturate(1.0h - abs(input.uv.x * 2.0h - 1.0h) * 6.0h);
                albedo *= .91h + .16h * rib;
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                // Read the existing reef shadow atlas at the displaced leaf position.
                // The world-position overload fades distant shadows to full light.
                Light light = GetMainLight(shadowCoord, input.positionWS, half4(1, 1, 1, 1));
                half lambert = saturate(dot(normal, light.direction) * .6h + .4h);
                half3 ambient = max(SampleSH(normal), half3(.14h, .19h, .18h));
                half transmission = saturate(dot(-normal, light.direction)) * h * .19h;
                half direct = light.distanceAttenuation * light.shadowAttenuation;
                half3 lit = albedo * (ambient + light.color * ((lambert * .82h + transmission) * direct));
                float extraDepth = max(0.0, -4.3 - input.positionWS.y);
                lit *= exp2(-extraDepth * half3(.065h, .026h, .010h));
                float fog = InitializeInputDataFog(float4(input.positionWS, 1.0), 0.0);
                return half4(MixFog(lit, fog), 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
