Shader "SwanUnderwater/SeabedSand"
{
    Properties
    {
        _BaseMap ("Sand grains", 2D) = "white" {}
        _BaseColor ("Sand tint", Color) = (0.68,0.75,0.78,1)
        _BumpMap ("Grain normal", 2D) = "bump" {}
        _BumpScale ("Normal strength", Float) = 0.28
        _CausticStrength ("Filtered water caustics", Range(0,1)) = 0.22
        _SanctuaryBlend ("Sanctuary pale sediment", Range(0,1)) = 0
        _WaterBottomColor ("Deep water background match", Color) = (.008,.065,.13,1)
        _CausticScale ("Caustic detail scale", Range(0.5,4)) = 1
        _GroundOcclusion ("Reef ground contact", 2D) = "white" {}
        _GroundOcclusionRect ("Contact origin XY and inverse extent ZW", Vector) = (-50,-50,0.01,0.01)
        _Metallic ("Metallic", Float) = 0
        _Smoothness ("Smoothness", Float) = 0.12
        _Cutoff ("Cutoff", Float) = 0.5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "OceanWaterHorizon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale;
            half _CausticStrength;
            half _SanctuaryBlend;
            half4 _WaterBottomColor;
            float _CausticScale;
            float4 _GroundOcclusionRect;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            // URP 17 selects static quality keywords on Android XR.
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_GroundOcclusion); SAMPLER(sampler_GroundOcclusion);
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                half4 sediment : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(i.normalOS, i.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = half4(n.tangentWS, i.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.sediment = i.color;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 grain = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                // Keep fine grains while reducing the source beach sand's yellow cast.
                half luminance = dot(grain, half3(0.2126,0.7152,0.0722));
                half3 albedo = lerp(luminance.xxx, grain, lerp(.40h,.15h,_SanctuaryBlend)) * _BaseColor.rgb * i.sediment.rgb;
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                half3 N = normalize(i.normalWS);
                half3 T = normalize(i.tangentWS.xyz);
                half3 B = cross(N,T) * i.tangentWS.w;
                N = normalize(TransformTangentToWorld(normalTS, half3x3(T,B,N)));
                if (_SanctuaryBlend > .001h)
                {
                    // Low, broad ripple normals preserve sand-grain detail. Both the
                    // ripple and cloud scale are world anchored, independent of gaze.
                    float ripplePhase=dot(i.positionWS.xz,float2(12.0,2.3))
                        + .3*sin(i.positionWS.z*.57);
                    // About half-metre ripples remain shallow; attenuate their normal
                    // once the projected wavelength approaches the pixel footprint.
                    float rippleFilter=1.0-smoothstep(.7,2.8,fwidth(ripplePhase));
                    float ripple=cos(ripplePhase)*rippleFilter;
                    N=normalize(N+half3(ripple*.065h,0,ripple*.0125h)*_SanctuaryBlend);
                    half sedimentPatch=sin(i.positionWS.x*.47+i.positionWS.z*.23)
                        *sin(i.positionWS.z*.69-i.positionWS.x*.12);
                    albedo*=lerp(half3(.91h,.95h,.96h),half3(1.04h,1.03h,.98h),sedimentPatch*.5h+.5h);
                }
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(i.positionWS));
                #else
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                #endif
                Light sun = GetMainLight(shadowCoord, i.positionWS, half4(1,1,1,1));
                half direct=saturate(dot(N,sun.direction))*sun.distanceAttenuation*sun.shadowAttenuation;
                half3 ambient=SampleSH(N);
                if (_SanctuaryBlend > .001h)
                {
                    // Cool diffuse bounce remains visible in shade without filling
                    // the directional reef shadows with yellow ambient light.
                    float2 contactUV=saturate((i.positionWS.xz-_GroundOcclusionRect.xy)*_GroundOcclusionRect.zw);
                    half contact=SAMPLE_TEXTURE2D(_GroundOcclusion,sampler_GroundOcclusion,contactUV).r;
                    // This one-time world-space contact field only occludes diffuse
                    // ambient. Real sun shadows and moving caustics keep their energy.
                    ambient=ambient*(.77h*contact)+half3(.025h,.055h,.067h);
                    half broadLight=.91h+.09h*sin(i.positionWS.x*.21+i.positionWS.z*.16)
                        *sin(i.positionWS.z*.28-i.positionWS.x*.09);
                    direct*=broadLight;
                }
                half3 illumination = ambient + sun.color * direct;
                // Continuous world-anchored waves avoid screen-space swimming or tiled square cells.
                float2 water = i.positionWS.xz * 1.8 + i.positionWS.y * float2(.18,-.12);
                float time = _Time.y * .18;
                float ridge;
                if (_SanctuaryBlend > .001h)
                {
                    // Fine, broken refracted light replaces the metre-wide connected
                    // bright lattice. Analytic filtering prevents distant VR shimmer.
                    water*=_CausticScale;
                    float wave=sin(water.x+sin(water.y*.73+time)*.63)
                        + sin(water.y*.97-sin(water.x*.81-time*.8)*.57);
                    float footprint=fwidth(wave);
                    float lines=1.0-smoothstep(.035,.14+footprint,abs(wave));
                    float patch=smoothstep(-.15,1.0,sin(water.x*.31+time*.3)
                        +.65*sin(water.y*.27-time*.2));
                    ridge=lines*patch*(1.0-saturate(footprint*.7));
                }
                else
                {
                    float wave = sin(dot(water,float2(1.31,1.79))+time)
                        + .62*sin(dot(water,float2(-1.83,1.17))-time*.81)
                        + .29*sin(dot(water,float2(3.11,-2.43))+time*.63);
                    ridge = 1.0-smoothstep(.015,.11+fwidth(wave),abs(wave));
                }
                float nearWater = 1.0-smoothstep(18,42,distance(i.positionWS,GetCameraPositionWS()));
                half focus = ridge * _CausticStrength * nearWater * saturate(N.y) * sun.shadowAttenuation;
                if (_SanctuaryBlend > .5h)
                {
                    // Deeper basins lose warm wavelengths and focused sunlight
                    // gradually. The fixed world height keeps this stable while swimming.
                    half extraDepth = max(0.0h, -i.positionWS.y - 4.3h);
                    illumination *= exp2(-extraDepth * half3(.065h, .026h, .010h));
                    focus *= exp2(-extraDepth * .065h);
                }
                illumination += lerp(half3(.66,.85,.79),half3(.83h,.94h,.92h),_SanctuaryBlend) * focus;
                float fog = InitializeInputDataFog(float4(i.positionWS, 1.0), 0.0);
                if (_SanctuaryBlend > .5h)
                {
                    // Water extinction follows real world distance in both stereo eyes.
                    // Keep the expanded floor continuously blue through the horizon,
                    // independent of the camera's projected/view-Z fog path.
                    float3 waterRay=i.positionWS-GetCameraPositionWS();
                    float waterDistance=length(waterRay);
                    float opticalDepth=waterDistance*unity_FogParams.x;
                    float transmission=saturate(exp2(-opticalDepth*opticalDepth));
                    half3 background=OceanWaterHorizon(unity_FogColor.rgb,_WaterBottomColor.rgb,
                        waterRay.y/max(.0001,waterDistance));
                    return half4(lerp(background,albedo*illumination,transmission),1);
                }
                return half4(MixFog(albedo * illumination, fog), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            // This opaque pass shares the exact material CBUFFER above. No alpha
            // test keywords are compiled, so it needs no additional texture reads.
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
    }
    Fallback Off
}
