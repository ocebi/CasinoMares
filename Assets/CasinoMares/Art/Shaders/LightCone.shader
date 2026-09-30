// Fake volumetric light under a pendant lamp: an open cone mesh drawn additively. Bright near the lamp,
// fading toward the floor, and soft at the silhouette edges where you look through less "air".
Shader "CasinoMares/LightCone"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 0.8, 0.55, 1)
        _Intensity ("Intensity", Float) = 0.12
        _Falloff ("Height Falloff", Float) = 1.8
        _EdgeSoftness ("Edge Softness", Float) = 2.0
        _NearFade ("Camera Near Fade (m)", Float) = 1.2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "LightCone"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _Falloff;
                half _EdgeSoftness;
                half _NearFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
                float viewDistance : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.uv = input.uv;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(positions.positionWS);
                output.viewDistance = length(GetCameraPositionWS() - positions.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half facing = abs(dot(normalize(input.normalWS), normalize(input.viewDirWS)));
                half edge = pow(saturate(facing), _EdgeSoftness);
                half height = pow(saturate(1.0 - input.uv.y), _Falloff);
                half near = saturate(input.viewDistance / max(_NearFade, 0.01));
                return half4(_Color.rgb * (_Intensity * edge * height * near), 1.0);
            }
            ENDHLSL
        }
    }
}
