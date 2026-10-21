Shader "SwanUnderwater/UnderwaterSkybox"
{
    Properties
    {
        _TopColor ("Surface Color (bright turquoise)", Color) = (0.30, 0.66, 0.68, 1)
        _HorizonColor ("Horizon Color (cyan-blue)", Color) = (0.045, 0.30, 0.38, 1)
        _BottomColor ("Bottom Color (deep dark)", Color) = (0.008, 0.07, 0.09, 1)
        _TopIntensity ("Surface Intensity", Float) = 1.05
        _HorizonIntensity ("Horizon Intensity", Float) = 0.9
        _SunColor ("Sun Shaft Color", Color) = (0.85, 0.98, 0.95, 1)
        _SunDirection ("Direction toward sun", Vector) = (-.32,.92,.18,0)
        _SurfaceDetail ("Surface light detail", Float) = 1
        [HideInInspector] _DepthWrite ("Finite background depth", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite [_DepthWrite]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"
            #include "OceanWaterHorizon.hlsl"

            struct appdata
            {
                float4 position : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldDir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _TopColor;
            float4 _HorizonColor;
            float4 _BottomColor;
            float _TopIntensity;
            float _HorizonIntensity;
            float4 _SunColor;
            float4 _SunDirection;
            float _SurfaceDetail;

            // Continuous waves have no square noise lattice for the caustic ridge
            // to expose. Non-aligned directions and slow domain warping keep the
            // surface organic without a texture or a high-frequency hash.
            void surfaceWaves(float2 surf, float time, out float waves, out float fine)
            {
                float2 warp = float2(
                    sin(dot(surf, float2(1.31, 1.73)) + time * 0.80),
                    sin(dot(surf, float2(-1.89, 1.11)) - time * 0.63));
                float2 p = surf + warp * 0.18;
                float height =
                    0.26 * sin(dot(p, float2(7.31, 5.17)) + time * 1.13) +
                    0.22 * sin(dot(p, float2(-4.87, 8.63)) - time * 0.91 + 1.7) +
                    0.18 * sin(dot(p, float2(10.19, -3.71)) + time * 0.73 + 3.1) +
                    0.14 * sin(dot(p, float2(2.43, 12.61)) - time * 1.27 + 4.6) +
                    0.12 * sin(dot(p, float2(-12.17, -7.13)) + time * 0.57 + 2.4) +
                    0.08 * sin(dot(p, float2(15.23, -11.47)) - time * 1.09 + 0.8);
                waves = 0.5 + height * 0.5;
                fine = 0.5 +
                    0.25 * sin(dot(p, float2(18.13, 15.71)) + time * 0.49) +
                    0.25 * sin(dot(p, float2(-20.37, 12.23)) - time * 0.83);
            }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position = UnityObjectToClipPos(v.position);
                // Interpolate the unnormalised direction; normalise per pixel so
                // the gradient and sun do not depend on the sky mesh tessellation.
                o.worldDir = mul(unity_ObjectToWorld, float4(v.position.xyz, 0)).xyz;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 d = normalize(i.worldDir);
                float y = d.y;

                // Match the scene fog at the horizon.
                float3 horizon = _HorizonColor.rgb * _HorizonIntensity;
                float3 col = OceanWaterHorizon(horizon, _BottomColor.rgb, y);
                if (y > 0.0)
                {
                    float t = smoothstep(0.02,0.90,y);
                    col = lerp(horizon,_TopColor.rgb*_TopIntensity,t);
                    // Below this band every ripple/sun contribution is zero. Avoid
                    // evaluating surface waves across the large forward-view water area.
                    [branch] if (y > 0.35)
                    {
                        float2 surf = d.xz/max(y,0.10)*3.5;
                        float time = _Time.y*0.11;
                        float waves, fine;
                        surfaceWaves(surf, time, waves, fine);
                        float surfaceMask = smoothstep(0.35,0.85,y);
                        // Refracted, broken highlights on the moving underside of the surface.
                        float ridge = saturate(1-abs(waves-.51)*6);
                        float ridge2 = ridge * ridge;
                        float ridges = ridge2 * ridge2 * ridge2;
                        col += float3(.10,.16,.17)*(ridges*.12+fine*.10)*surfaceMask*_SurfaceDetail;
                        col += float3(.05,.08,.085)*(waves-.5)*surfaceMask*_SurfaceDetail;
                        float3 sunDir=normalize(_SunDirection.xyz);
                        float cosSun=saturate(dot(d,sunDir));
                        float glow=pow(cosSun,18)*.20;
                        float core=pow(cosSun,190)*.58;
                        col += _SunColor.rgb*(glow+core)*surfaceMask*(.73+.27*waves);
                        // World-space shafts below the surface are separate geometry, providing stereo parallax.
                    }
                }
                col = saturate(col);
                return float4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
