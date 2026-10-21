// Editor-only validation: explicit mip texel decode, no filtering or normal swizzle.
Shader "Hidden/SwanUnderwater/ReefAstcReadback"
{
    Properties { _Source("Source", 2D) = "white" {} }
    SubShader
    {
        Pass
        {
            ZWrite Off ZTest Always Cull Off Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            Texture2D<float4> _Source;
            int _Mip;
            float _EncodeSrgb;
            float4 frag(v2f_img input) : SV_Target
            {
                // SV_Position addresses the exact destination pixel. CPU calibration detects
                // any backend row orientation difference against independently known RGBA bytes.
                float4 pixel = _Source.Load(int3(int2(input.pos.xy), _Mip));
                if (_EncodeSrgb > 0.5)
                {
                    float3 low = pixel.rgb * 12.92;
                    float3 high = 1.055 * pow(max(pixel.rgb, 0.0), 1.0 / 2.4) - 0.055;
                    pixel.rgb = lerp(low, high, step(0.0031308, pixel.rgb));
                }
                return pixel;
            }
            ENDHLSL
        }
    }
}
