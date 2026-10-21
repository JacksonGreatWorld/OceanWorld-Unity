Shader "SwanUnderwater/CollectibleSurface"
{
    Properties
    {
        _ProximityGlow ("Soft nearby highlight", Range(0,1)) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 4
        [Toggle] _ZWrite ("Depth write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "CollectibleForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back
            ZTest [_ZTest]
            ZWrite [_ZWrite]
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
            UNITY_INSTANCING_BUFFER_START(CollectibleProps)
                UNITY_DEFINE_INSTANCED_PROP(float, _ProximityGlow)
            UNITY_INSTANCING_BUFFER_END(CollectibleProps)
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 kind : TEXCOORD1;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float2 kind : TEXCOORD3;
                half4 color : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv; o.kind = v.kind; o.color = v.color;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float kind = floor(i.kind.x + .5);
                float exterior = 1.0 - saturate(i.kind.y);
                half coin = step(4.5, kind);
                half cowrie = step(1.5, kind) * (1.0 - step(2.5, kind));
                float growthPhase = i.uv.y * 85.0;
                if (kind < .5) growthPhase = i.uv.y * 90.0 + sin(i.uv.x * 9.0) * .6;
                if (kind > .5 && kind < 1.5 || kind > 3.5 && kind < 4.5) growthPhase = i.uv.x * 210.0;
                float growth = sin(growthPhase) * saturate(1.0 - fwidth(growthPhase) * .35);
                float spots = sin(i.uv.x * 89.0 + sin(i.uv.y * 19.0) * 2.0) * sin(i.uv.y * 73.0 + sin(i.uv.x * 25.0));
                float spotMask = smoothstep(.48, .68, spots) * saturate(1.0 - max(fwidth(i.uv.x * 89.0), fwidth(i.uv.y * 73.0)));
                half3 albedo = i.color.rgb * (1.0 + .055 * growth * exterior * (1.0 - coin) * (1.0 - cowrie));
                albedo *= 1.0 - cowrie * exterior * spotMask * .48;
                float3 normal = normalize(i.normalWS);
                float relief = growth * exterior * (1.0 - coin) * (1.0 - cowrie) * .000055;
                float3 dpdx = ddx(i.positionWS), dpdy = ddy(i.positionWS);
                float3 r1 = cross(dpdy, normal), r2 = cross(normal, dpdx);
                float determinant = dot(dpdx, r1);
                normal = normalize(normal - (ddx(relief) * r1 + ddy(relief) * r2) * (sign(determinant) / max(abs(determinant), .0000001)));
                InputData lighting = (InputData)0;
                lighting.positionWS = i.positionWS;
                lighting.normalWS = normal;
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    lighting.shadowCoord = ComputeScreenPos(TransformWorldToHClip(i.positionWS));
                #else
                    lighting.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                #endif
                lighting.bakedGI = max(SampleSH(normal), half3(.13, .15, .14));
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                lighting.shadowMask = half4(1,1,1,1);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = saturate(i.color.a);
                surface.smoothness = lerp(lerp(.27, .68, cowrie), .74, coin);
                surface.normalTS = half3(0,0,1); surface.occlusion = 1; surface.alpha = 1;
                half glow = saturate(UNITY_ACCESS_INSTANCED_PROP(CollectibleProps, _ProximityGlow));
                half rim = 1.0 - saturate(dot(normal, lighting.viewDirectionWS));
                surface.emission = albedo * coin * .07 + glow * (albedo * .15 + half3(.11,.25,.23) * rim * rim * .75);
                half4 color = UniversalFragmentPBR(lighting, surface);
                float fog = InitializeInputDataFog(float4(i.positionWS,1), 0);
                return half4(MixFog(color.rgb, fog), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
