// The seaside sky: a photographed sunset (a Poly Haven HDRI prepared by Tools/prepare_sky.py) plus a halo and disk
// that give the sun back the brightness 8 bits can't hold. The skybox (SunsetSkybox.shader) draws it and the sea
// (Water.shader) reflects it and fades into its horizon, so the two always meet without a seam.
// The including shader declares these (the builder sets them to the same values on both materials):
//   TEXTURE2D(_SkyTex); SAMPLER(sampler_SkyTex);  the photo's upper half: u round the horizon, v from it to the zenith
//   float4 _SkyParams;     x: turns to rotate the photo by, y: brightness
//   half4 _SunColor;       HDR
//   float4 _SunDirection;  xyz: toward the sun in the photo
//   float4 _SunGlow;       x: halo strength, y: halo tightness (power), z: disk strength, w: disk radius (radians)
#ifndef CASINOMARES_SUNSET_SKY_INCLUDED
#define CASINOMARES_SUNSET_SKY_INCLUDED

// The photo in this direction (below the horizon: the horizon). It's stored as x / (1 + x); undo that.
half3 SkyPhoto(float3 direction, float lod)
{
    float u = frac(atan2(direction.x, direction.z) * (0.5 / PI) + _SkyParams.x);
    float v = asin(saturate(direction.y)) * (2.0 / PI);
    half3 encoded = SAMPLE_TEXTURE2D_LOD(_SkyTex, sampler_SkyTex, float2(u, v), lod).rgb;
    return encoded / max(1.0 - encoded, 0.02) * _SkyParams.y;
}

// sunDisk scales the sun itself (the sea leaves it out: its glitter is the sun's reflection). lod blurs the photo:
// 0 for the sky, more for the rippled sea's reflection.
half3 SunsetSky(float3 direction, half sunDisk, float lod)
{
    half3 sky = SkyPhoto(direction, lod);
    float sunDot = saturate(dot(direction, _SunDirection.xyz));
    sky += _SunColor.rgb * (pow(sunDot, _SunGlow.y) * _SunGlow.x);
    float disk = smoothstep(cos(_SunGlow.w), cos(_SunGlow.w * 0.8), sunDot);
    sky += _SunColor.rgb * (disk * _SunGlow.z * sunDisk);
    return sky;
}

#endif
