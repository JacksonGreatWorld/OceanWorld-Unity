// A local luminous water-ripple cue. No scene-wide bloom, depth texture or global time.
Shader "SwanUnderwater/ActivityRippleGate"
{
    Properties
    {
        [HDR] _BaseColor ("Water light", Color) = (.24, .76, 1, 1)
        _RipplePhase ("Owner-driven wave phase", Float) = 0
        _PassProgress ("Passing dissolve", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "WaterRippleForward"
            Tags { "LightMode"="UniversalForward" }
            // Premultiplied additive light, depth-occluded by the real reef.
            Blend One One, One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 radial : TEXCOORD0;
                half fog : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _RipplePhase;
                half _PassProgress;
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.radial = input.uv;
                output.fog = ComputeFogFactor(output.positionHCS.z);
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float phase = _RipplePhase * TWO_PI;
                float radius = length(input.radial);
                float water = sin(input.radial.x * 11.0 + phase) * sin(input.radial.y * 9.0 - phase * 2.0);
                float coreDistance = abs(radius - (1.0 + water * .008));
                half core = 1.0 - smoothstep(.007, .022, coreDistance);
                half halo = exp2(-coreDistance * coreDistance * 310.0);
                half waves = 0;
                [unroll] for (int band = 0; band < 3; band++)
                {
                    float age = frac(_RipplePhase + band / 3.0);
                    float front = 1.0 + age * .28 + water * .005;
                    float distance = abs(radius - front);
                    half ring = 1.0 - smoothstep(.006, .016, distance);
                    waves += ring * sin(age * PI);
                }
                // A final outward wave travels with the short passing dissolve.
                float passDistance = abs(radius - (1.0 + _PassProgress * .26));
                half passing = (1.0 - smoothstep(.008, .023, passDistance)) * sin(_PassProgress * PI);
                half edge = smoothstep(.62, .69, radius) * (1.0 - smoothstep(1.31, 1.38, radius));
                half3 light = _BaseColor.rgb * (halo * .55 + waves * .72 + passing * .65)
                    + half3(.65, 1.08, 1.35) * core * (1.05 + .06 * sin(phase));
                light *= _BaseColor.a * edge;
                // Additive fog must fade to black, not add a fog-colored annulus.
                light = MixFogColor(light, half3(0,0,0), input.fog);
                return half4(light, saturate(_BaseColor.a * edge * max(core, waves)));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
