// The seaside casino's sky: a photographed sunset with the sun low over the sea (see SunsetSky.hlsl).
Shader "CasinoMares/SunsetSkybox"
{
    Properties
    {
        [NoScaleOffset] _SkyTex ("Sky Photo (upper half, x/(1+x))", 2D) = "black" {}
        _SkyParams ("Sky (rotation in turns, brightness)", Vector) = (0, 1, 0, 0)
        [HDR] _SunColor ("Sun", Color) = (1, 0.7, 0.45, 1)
        _SunDirection ("Toward the Sun", Vector) = (0.9, 0.04, 0.42, 0)
        _SunGlow ("Sun (halo, halo tightness, disk, disk radius)", Vector) = (0.6, 400, 12, 0.012)
        _SunDiskIntensity ("Sun Disk Scale", Float) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _SkyParams;
                half4 _SunColor;
                float4 _SunDirection;
                float4 _SunGlow;
                half _SunDiskIntensity;
            CBUFFER_END

            TEXTURE2D(_SkyTex);
            SAMPLER(sampler_SkyTex);

            #include "SunsetSky.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(SunsetSky(normalize(input.direction), _SunDiskIntensity, 0.0), 1.0);
            }
            ENDHLSL
        }
    }
}
