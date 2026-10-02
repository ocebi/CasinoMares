// The sea around the seaside casino. Near the terrace three Gerstner waves give it a real swell (they die away
// further out, where the mesh is coarse); two scrolling normal maps add the ripples. The sunset sky is reflected with
// a Fresnel blend, the low sun lays a glittering path across it and shines through the wave crests, and a haze fades
// it into the sky photo's horizon (sky and sea both use SunsetSky.hlsl, so there's no seam). Opaque, and it needs no
// depth or opaque texture, so it runs at the Mobile quality level.
Shader "CasinoMares/Water"
{
    Properties
    {
        _ShallowColor ("Scatter (looking down)", Color) = (0.05, 0.3, 0.34, 1)
        _DeepColor ("Deep (grazing)", Color) = (0.012, 0.05, 0.1, 1)
        [NoScaleOffset] [Normal] _NormalMap ("Ripples", 2D) = "bump" {}
        _WaveTiling ("Ripple Tiling per Metre (layer 1, layer 2)", Vector) = (0.07, 0.19, 0, 0)
        _WaveSpeed ("Ripple Scroll (layer 1 xy, layer 2 zw)", Vector) = (-0.02, -0.006, -0.012, 0.014)
        _WaveStrength ("Ripple Strength", Float) = 0.55
        _CalmDistance ("Ripples Calm Beyond (m)", Float) = 1200
        _SwellA ("Swell A (direction xy, steepness, wavelength)", Vector) = (-1, -0.25, 0.07, 14)
        _SwellB ("Swell B", Vector) = (-0.8, 0.5, 0.06, 9)
        _SwellC ("Swell C", Vector) = (-0.9, -0.7, 0.05, 5.5)
        _SwellCenter ("Swell Center (xz) and Fade Start/End (m)", Vector) = (14, 3, 90, 180)
        _Glitter ("Sparkle Sharpness", Float) = 2400
        [HDR] _GlitterColor ("Sparkle", Color) = (30, 20, 12, 1)
        _SunPath ("Sun Path (sharpness near, far, strength)", Vector) = (300, 60, 3, 0)
        _Scatter ("Light Through the Crests", Float) = 0.6
        _HazeDensity ("Haze Density", Float) = 0.0012

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
            half4 _ShallowColor;
            half4 _DeepColor;
            float4 _WaveTiling;
            float4 _WaveSpeed;
            half _WaveStrength;
            float _CalmDistance;
            float4 _SwellA;
            float4 _SwellB;
            float4 _SwellC;
            float4 _SwellCenter;
            half _Glitter;
            half4 _GlitterColor;
            float4 _SunPath;
            half _Scatter;
            float _HazeDensity;
            float4 _SkyParams;
            half4 _SunColor;
            float4 _SunDirection;
            float4 _SunGlow;
        CBUFFER_END

        // One Gerstner wave: moves the point in a circle (crests sharpen, troughs flatten) and adds its slope to the
        // tangents. wave = (direction xy, steepness, wavelength); fade scales it away from the terrace.
        float3 Gerstner(float4 wave, float3 p, float fade, inout float3 tangent, inout float3 binormal)
        {
            float steepness = wave.z * fade;
            float k = 2.0 * PI / wave.w;
            float speed = sqrt(9.8 / k);
            float2 d = normalize(wave.xy);
            float f = k * (dot(d, p.xz) - speed * _Time.y);
            float a = steepness / k;
            float s = sin(f);
            float c = cos(f);
            tangent += float3(-d.x * d.x * steepness * s, d.x * steepness * c, -d.x * d.y * steepness * s);
            binormal += float3(-d.x * d.y * steepness * s, d.y * steepness * c, -d.y * d.y * steepness * s);
            return float3(d.x * a * c, a * s, d.y * a * c);
        }

        // The swell at a world position: the moved position, its normal, and the height (for the crests).
        float3 Swell(float3 positionWS, out float3 normalWS, out float height)
        {
            float distance = length(positionWS.xz - _SwellCenter.xy);
            float fade = 1.0 - smoothstep(_SwellCenter.z, _SwellCenter.w, distance);
            float3 tangent = float3(1.0, 0.0, 0.0);
            float3 binormal = float3(0.0, 0.0, 1.0);
            float3 offset = Gerstner(_SwellA, positionWS, fade, tangent, binormal);
            offset += Gerstner(_SwellB, positionWS, fade, tangent, binormal);
            offset += Gerstner(_SwellC, positionWS, fade, tangent, binormal);
            normalWS = normalize(cross(binormal, tangent));
            height = offset.y;
            return positionWS + offset;
        }

        struct DepthAttributes
        {
            float4 positionOS : POSITION;
        };

        float4 DepthVert(DepthAttributes input) : SV_POSITION
        {
            float3 normal;
            float height;
            return TransformWorldToHClip(Swell(TransformObjectToWorld(input.positionOS.xyz), normal, height));
        }
        ENDHLSL

        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);
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
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float height : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 normal;
                float height;
                float3 positionWS = Swell(TransformObjectToWorld(input.positionOS.xyz), normal, height);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = normal;
                output.height = height;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 positionWS = input.positionWS;
                float3 toCamera = GetCameraPositionWS() - positionWS;
                float distance = length(toCamera);
                float3 view = toCamera / distance;

                // Ripples on top of the swell. The map's tangent space has z up; the sea lies flat, so tangent x/y
                // are world x/z.
                float2 uv = positionWS.xz;
                half3 a = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv * _WaveTiling.x + _WaveSpeed.xy * _Time.y), _WaveStrength);
                half3 b = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv * _WaveTiling.y + _WaveSpeed.zw * _Time.y), _WaveStrength);
                half2 ripple = (a.xy + b.xy) * (1.0 - saturate(distance / _CalmDistance));
                float3 normal = normalize(normalize(input.normalWS) + float3(ripple.x, 0.0, ripple.y));

                half facing = saturate(dot(normal, view));
                half fresnel = 0.02 + 0.98 * pow(1.0 - facing, 5.0);
                float3 reflected = reflect(-view, normal);
                reflected.y = abs(reflected.y);
                half3 sky = SunsetSky(reflected, 0.0, _SkyParams.z);

                // Shade from the sun light, but glitter where the sun is in the photo (the light sits a little higher,
                // so the hall and terrace get some of it).
                Light sun = GetMainLight(TransformWorldToShadowCoord(positionWS));
                half lit = sun.shadowAttenuation;
                float3 toSun = _SunDirection.xyz;

                // The water itself: dark where you look along it, a deep teal where you look into it.
                half3 body = lerp(_DeepColor.rgb, _ShallowColor.rgb, facing * facing);
                body *= 0.5 + 0.5 * lit;

                // Looking toward the sun, it shines through the thin tops of the waves.
                half crest = saturate(input.height * 3.0 + 0.5);
                half through = pow(saturate(dot(-view, toSun) * 0.5 + 0.5), 6.0);
                body += _ShallowColor.rgb * _SunColor.rgb * (through * crest * _Scatter * lit);

                // The sun's path: a lobe that widens with distance (far ripples average out in the mips), plus
                // sharp sparkles on the nearer ripples.
                half pathSharpness = lerp(_SunPath.x, _SunPath.y, saturate(distance / 400.0));
                half path = pow(saturate(dot(reflected, toSun)), pathSharpness) * _SunPath.z;
                float3 halfway = normalize(toSun + view);
                half sparkle = pow(saturate(dot(normal, halfway)), _Glitter);
                half3 glitter = (_SunColor.rgb * path + _GlitterColor.rgb * sparkle) * lit;

                half3 color = lerp(body, sky, fresnel) + glitter;

                // Haze: blend toward the sky's own horizon in this direction, which is what the skybox shows there.
                float3 horizon = normalize(float3(-view.x, 0.0, -view.z) + float3(0.0, 1e-4, 0.0));
                half haze = 1.0 - exp(-_HazeDensity * distance);
                color = lerp(color, SunsetSky(horizon, 0.0, 4.0), haze);
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

            half frag() : SV_Target
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

            // Close enough for SSAO: the sea is nearly flat.
            half4 frag() : SV_Target
            {
                return half4(NormalizeNormalPerPixel(float3(0.0, 1.0, 0.0)), 0.0);
            }
            ENDHLSL
        }
    }
}
