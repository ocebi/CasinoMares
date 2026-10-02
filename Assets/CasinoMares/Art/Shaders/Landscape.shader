// The land out at sea: the islands, their trees and villages, and the mountains along the far coast. The builder
// bakes the colours into the vertices (sRGB; alpha: 0 ground and trees, 0.5 buildings, 0.75 the windows, shutters and
// doors on their walls, 1 lit windows), and this lights them with the low sun and the sky, breaks them up with a
// little detail noise, lays a line of surf where they meet the sea, and fades them into the sky photo's horizon
// through a haze that hangs low over the water (sky and sea use SunsetSky.hlsl too, so the far land melts into
// exactly the sky behind it). Opaque, no shadows (it's all far beyond the shadow distance).
Shader "CasinoMares/Landscape"
{
    Properties
    {
        [NoScaleOffset] _DetailTex ("Detail Noise (linear, 0.5 = no change)", 2D) = "grey" {}
        _Detail ("Detail (tiling per metre: fine, coarse; strength: fine, coarse)", Vector) = (0.09, 0.011, 0.4, 0.3)
        _Wrap ("Light Wrap", Float) = 0.15
        _Rim ("Backlit Rim", Float) = 0.7
        [HDR] _WindowColor ("Lit Windows", Color) = (6, 3.4, 1.4, 1)
        _Shore ("Shore (sea level, surf height, wet height, surf strength)", Vector) = (-3, 0.45, 1.6, 0.7)
        _Haze ("Haze (density at sea level, height scale m, strength)", Vector) = (0.0012, 100, 1, 0)
        _PanelPull ("Wall Panels Toward Camera (per metre squared of distance)", Float) = 0.000003

        [NoScaleOffset] _SkyTex ("Sky Photo (upper half, x/(1+x))", 2D) = "black" {}
        _SkyParams ("Sky (rotation in turns, brightness, reflection blur)", Vector) = (0, 1, 1.5, 0)
        [HDR] _SunColor ("Sun", Color) = (1, 0.7, 0.45, 1)
        _SunDirection ("Toward the Sun", Vector) = (0.9, 0.04, 0.42, 0)
        _SunGlow ("Sun (halo, halo tightness, disk, disk radius)", Vector) = (0.6, 400, 12, 0.012)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Detail;
            half _Wrap;
            half _Rim;
            half4 _WindowColor;
            float4 _Shore;
            float4 _Haze;
            float _PanelPull;
            float4 _SkyParams;
            half4 _SunColor;
            float4 _SunDirection;
            float4 _SunGlow;
        CBUFFER_END

        struct DepthAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
        };

        struct DepthVaryings
        {
            float4 positionCS : SV_POSITION;
            float3 normalWS : TEXCOORD0;
        };

        // Windows, shutters and doors lie a few centimetres off their walls: far closer than the depth buffer can tell
        // apart a few hundred metres away (WebGL has no reversed Z), so they flickered through the wall. Sliding them
        // toward the camera along its line of sight leaves them in place on screen but safely in front of the wall.
        float3 PullPanel(float3 positionWS, half alpha)
        {
            float3 toCamera = GetCameraPositionWS() - positionWS;
            float distance = length(toCamera);
            float pull = alpha > 0.6 ? min(distance * distance * _PanelPull, distance * 0.5) : 0.0;
            return positionWS + toCamera / max(distance, 1e-4) * pull;
        }

        DepthVaryings DepthVert(DepthAttributes input)
        {
            DepthVaryings output;
            output.positionCS = TransformWorldToHClip(PullPanel(TransformObjectToWorld(input.positionOS.xyz), input.color.a));
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "Landscape"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_DetailTex);
            SAMPLER(sampler_DetailTex);
            TEXTURE2D(_SkyTex);
            SAMPLER(sampler_SkyTex);

            #include "SunsetSky.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                half3 ambient : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = PullPanel(TransformObjectToWorld(input.positionOS.xyz), input.color.a);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = half4(SRGBToLinear(input.color.rgb), input.color.a);
                output.ambient = SampleSH(output.normalWS);
                return output;
            }

            half Detail(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, uv).r * 2.0 - 1.0;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 positionWS = input.positionWS;
                float3 toCamera = GetCameraPositionWS() - positionWS;
                float distance = length(toCamera);
                float3 view = toCamera / distance;
                float3 normal = normalize(input.normalWS);

                half built = saturate(input.color.a * 4.0);
                half window = saturate((input.color.a - 0.85) * 6.67);

                // Detail: fine grain and big patches, from above on the slopes and from the side on the cliffs; only
                // a trace of it on the buildings.
                float2 side = abs(normal.x) > abs(normal.z) ? positionWS.zy : positionWS.xy;
                float2 uv = lerp(side, positionWS.xz, step(0.5, abs(normal.y)));
                half detail = (Detail(uv * _Detail.x) * _Detail.z + Detail(uv * _Detail.y + 0.37) * _Detail.w) * (1.0 - built * 0.85);
                half3 albedo = input.color.rgb * saturate(1.0 + detail);

                // Wet, darker rock just above the water, and a line of surf where the sea meets it.
                float above = positionWS.y - _Shore.x;
                albedo *= lerp(0.55, 1.0, smoothstep(0.0, _Shore.z, above));
                half surf = (1.0 - smoothstep(_Shore.y * 0.3, _Shore.y, above + detail * 0.15)) * _Shore.w;
                albedo = lerp(albedo, half3(0.8, 0.8, 0.78), saturate(surf));

                Light sun = GetMainLight();
                half lambert = saturate((dot(normal, sun.direction) + _Wrap) / (1.0 + _Wrap));
                half3 color = albedo * (sun.color * lambert + input.ambient);

                // Seen against the low sun, edges glow: light scattering through leaves and the haze round them.
                half edge = pow(1.0 - saturate(dot(normal, view)), 3.0);
                half behind = pow(saturate(dot(-view, sun.direction)), 3.0);
                color += sun.color * (albedo + 0.04) * (edge * behind * _Rim);

                color += _WindowColor.rgb * window;

                // Haze, thickest down on the water: the optical depth to this point through air that thins out with
                // height (exponentially, over _Haze.y metres), toward the sky right behind it.
                float h0 = max(GetCameraPositionWS().y - _Shore.x, 0.0);
                float h1 = max(above, 0.0);
                float rise = h1 - h0;
                float thinning = abs(rise) > 0.01 ? _Haze.y * (exp(-h0 / _Haze.y) - exp(-h1 / _Haze.y)) / rise : exp(-h0 / _Haze.y);
                half haze = (1.0 - exp(-_Haze.x * distance * thinning)) * _Haze.z;
                float3 behindIt = normalize(float3(-view.x, max(-view.y, 0.0), -view.z) + float3(0.0, 1e-4, 0.0));
                color = lerp(color, SunsetSky(behindIt, 0.0, 4.0), haze);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment frag

            half frag(DepthVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment frag

            half4 frag(DepthVaryings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(normalize(input.normalWS)), 0.0);
            }
            ENDHLSL
        }
    }
}
