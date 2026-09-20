#ifndef DRIFT_CLOUDS_INCLUDED
#define DRIFT_CLOUDS_INCLUDED

// Global uniforms pushed by Drift.Visuals.CloudShadows via Shader.SetGlobal*. Not material
// properties on purpose: every shader that includes this file gets the same drifting shadow, and
// with the globals left at zero CloudShadow() returns 1 (no clouds).
float  _CloudCover;
float4 _CloudOffset;
float  _CloudScale;
float  _CloudShadowStrength;

float DriftHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float DriftNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = DriftHash(i);
    float b = DriftHash(i + float2(1, 0));
    float c = DriftHash(i + float2(0, 1));
    float d = DriftHash(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// 0..1 cloud density at a world XZ position (1 = under a cloud).
float CloudDensity(float2 wp)
{
    float2 p = wp * _CloudScale + _CloudOffset.xy;
    float c = DriftNoise(p) * 0.62 + DriftNoise(p * 2.7 + 17.3) * 0.38;
    float cover = saturate(_CloudCover);
    return smoothstep(1.0 - cover, 1.0 - cover + 0.32, c);
}

// Multiplier for lit colour: 1 in the open, (1 - _CloudShadowStrength) under a cloud.
float CloudShadow(float2 wp)
{
    return 1.0 - CloudDensity(wp) * _CloudShadowStrength;
}

#endif
