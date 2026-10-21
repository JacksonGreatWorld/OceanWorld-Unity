Shader "SwanUnderwater/SanctuaryVista"
{
    Properties
    {
        _Visibility ("Relief contrast through water", Range(0,1)) = .46
        _Density ("Distant water attenuation per metre", Float) = .0065
        _SurfaceTint ("Matching sanctuary upper water", Color) = (.315,.6825,.8295,1)
        _DepthTint ("Matching sanctuary lower water", Color) = (.008,.065,.13,1)
        _HorizonIntensity ("Matching sky horizon intensity", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "DistantBlueRelief"
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _SurfaceTint;
                half4 _DepthTint;
                half _Visibility;
                float _Density;
                half _HorizonIntensity;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 relief : COLOR;
                half height : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.relief = input.color;
                output.height = input.uv.x;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 toSurface = input.positionWS - GetCameraPositionWS();
                float distanceToSurface = max(length(toSurface), .001);
                half viewElevation = toSurface.y / distanceToSurface;
                // Use the same horizon-to-surface gradient as the sanctuary sky.
                // An opaque skirt therefore dissolves into blue water, not a visible
                // rectangular fog card, flat black mountain or hard ground seam.
                half3 horizon = unity_FogColor.rgb * _HorizonIntensity;
                half3 water = lerp(horizon, _DepthTint.rgb,
                    smoothstep(0.0h, .65h, -viewElevation));
                water = lerp(water, _SurfaceTint.rgb,
                    smoothstep(.02h, .90h, viewElevation));
                half3 normal = normalize(input.normalWS);
                Light sun = GetMainLight();
                half sunFacing = saturate(dot(normal, sun.direction));
                // Keep relief in the live water's colour space. An absolute RGB floor
                // can overpower colour-managed fog and flatten this into a pale cutout.
                // This positive relative light keeps blue bounce, while real normals,
                // sun direction and baked erosion retain subdued geological relief.
                half reliefLight = .52h + .28h * sunFacing
                    + .10h * input.relief.r + .04h * saturate(normal.y);
                half3 pigment = water * reliefLight;
                // The close-reef ExpSquared fog is much denser. Reusing it beyond
                // 180 m erases all distant silhouettes. This low-contrast attenuation
                // is calibrated for the inaccessible vista, not nearby gameplay assets.
                half visibility = _Visibility * exp2(-distanceToSurface * _Density);
                visibility *= smoothstep(.015h,.12h,input.height);
                return half4(lerp(water,pigment,visibility),1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
