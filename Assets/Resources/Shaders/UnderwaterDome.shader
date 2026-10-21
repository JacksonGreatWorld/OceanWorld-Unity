// UnderwaterDome.shader
// Inverted sphere background dome. Samples vertical gradient texture.
// Cull Front so we see inside. Simple, reliable, no procedural normal calc.
// Gradient texture is vertical-only (all columns identical) → no UV seam at back.

Shader "SwanUnderwater/UnderwaterDome"
{
    Properties
    {
        _BaseMap ("Gradient", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Background"
            "Queue" = "Background"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Front
        ZWrite Off

        Pass
        {
            Name "Dome"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
