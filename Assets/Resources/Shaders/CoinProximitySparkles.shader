Shader "SwanUnderwater/CoinProximitySparkles"
{
    Properties
    {
        _Near ("Nearby strength", Range(0,1)) = 0
        _Clock ("Animation clock", Float) = 0
        _ViewRight ("Centre eye right", Vector) = (1,0,0,0)
        _ViewUp ("Centre eye up", Vector) = (0,1,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "NearbyGold"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZTest LEqual
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Near, _Clock;
                float4 _ViewRight, _ViewUp;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 glint : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half2 style : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                half isGlint = step(.5, input.glint.x);
                float phase = input.glint.x * 2.399963;
                float pulse = pow(saturate(sin(_Clock * (1.35 + input.glint.x * .09) + phase)), 8);
                float angle = phase + _Clock * .12;
                float s = sin(angle), c = cos(angle);
                float2 corner = float2(c * input.uv.x - s * input.uv.y, s * input.uv.x + c * input.uv.y);
                if (isGlint > .5)
                    world += (_ViewRight.xyz * corner.x + _ViewUp.xyz * corner.y) * input.glint.y * (.70 + .30 * pulse);
                else
                    world += TransformObjectToWorldNormal(input.normalOS) * .00015;
                output.positionCS = TransformWorldToHClip(world);
                output.positionWS = world;
                output.uv = input.uv;
                output.style = half2(isGlint, pulse);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 p = abs(input.uv);
                float core = exp2(-30 * dot(p,p));
                float rays = exp2(-8 * min(p.x,p.y)) * pow(saturate(1 - max(p.x,p.y)), 3);
                float star = saturate(core + rays * .72);
                float breath = .13 + .055 * sin(_Clock * 1.65);
                float brightness = lerp(breath, star * input.style.y * 1.6, input.style.x) * _Near;
                half3 gold = lerp(half3(.65,.32,.055), half3(1.2,.95,.48), input.style.x);
                float fog = InitializeInputDataFog(float4(input.positionWS,1), 0);
                // Additive light fades to zero through fog, rather than adding a fog-coloured rectangle.
                half3 colour = MixFogColor(gold * brightness, half3(0,0,0), fog);
                return half4(colour, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
