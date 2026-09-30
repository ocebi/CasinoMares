// Extra pass on the cat fur: a warm, top-weighted rim light added over the lit fur, so the cats (especially the
// dark ones) read against the dim lounge. Used as a second material on the cat's SkinnedMeshRenderer.
Shader "CasinoMares/CatRim"
{
    Properties
    {
        [HDR] _Color ("Rim Color", Color) = (1, 0.78, 0.52, 1)
        _Intensity ("Intensity", Float) = 0.35
        _Power ("Rim Power", Float) = 3.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "CatRim"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _Power;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(positions.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                half rim = pow(1.0 - saturate(dot(normal, normalize(input.viewDirWS))), _Power);
                // The lamps hang overhead, so light the upper edges more than the belly.
                half top = saturate(normal.y * 0.6 + 0.45);
                return half4(_Color.rgb * (rim * top * _Intensity), 1.0);
            }
            ENDHLSL
        }
    }
}
