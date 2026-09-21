#ifndef DRIFT_CLOUDS_INCLUDED
#define DRIFT_CLOUDS_INCLUDED

// Global uniforms pushed by Drift.Visuals.CloudShadows via Shader.SetGlobal*. Not material
// properties on purpose: every shader that includes this file gets the same drifting shadow, and
// with the globals left at zero CloudShadow() returns 1 (no clouds).
//
// The clouds are clumps ("Schaefchenwolken") on a jittered grid in cloud space q = (xz + _CloudShadowShift.xy) *
// _CloudScale + _CloudOffset.xy (one cell per _CloudScale^-1 world units, the offset drifts with the wind). Cell id
// holds at most one clump; DriftCloudClump is the single definition of it: Drift/Clouds builds its puffs from it in
// the vertex shader, CloudDensity below shades the ground under it, Drift.Visuals.CloudField mirrors it in C#.
// A clump's centre lies in the middle 0.4 of its cell and it reaches at most 0.45 cells from it, so the 2x2 cells
// nearest to a point are all that can cover it.
float  _CloudCover;
float4 _CloudOffset;
float  _CloudScale;
float  _CloudShadowStrength;
float4 _CloudShadowShift;   // xy = world offset from a ground point to the cloud that shades it (towards the sun)

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

// Clump of cell id (integer cloud-space coordinates) at the given cover: centre in cloud space, radius in cells
// (0 = no clump; it grows smoothly while the cover passes the cell's own threshold), long axis (cos, sin) and the
// stretch along it. The puffs sit at the centre and on a ring of six at 60 degree steps from the axis.
void DriftCloudClump(float2 id, float cover, out float2 centre, out float radius, out float2 axis, out float aspect)
{
    float h1 = DriftHash(id);
    float h2 = DriftHash(id + float2(17.31, 5.17));
    float h3 = DriftHash(id + float2(3.71, 41.9));
    float h4 = DriftHash(id + float2(29.3, 11.1));
    float share = saturate(cover * 1.9);
    float grow = saturate((share - h1) * 6.0);
    radius = (0.16 + 0.12 * h2) * grow;
    centre = id + 0.3 + 0.4 * float2(h3, h4);
    float a = (h2 + h4) * 3.14159;
    axis = float2(cos(a), sin(a));
    aspect = 1.0 + 0.5 * h3;
}

float DriftClumpDensity(float2 q, float2 id, float cover)
{
    float2 c, axis;
    float r, aspect;
    DriftCloudClump(id, cover, c, r, axis, aspect);
    float2 d = q - c;
    float2 l = float2(dot(d, axis) / aspect, dot(d, float2(-axis.y, axis.x)));
    float len2 = dot(l, l);
    float len = sqrt(len2);
    // cos(6 angle) from the axis via the Chebyshev polynomial: six soft lobes, roughly where the ring puffs are.
    float c2 = l.x * l.x / max(len2, 1e-8);
    float t6 = ((32.0 * c2 - 48.0) * c2 + 18.0) * c2 - 1.0;
    float edge = r * (0.95 + 0.05 * t6);
    return (1.0 - smoothstep(edge * 0.35, edge * 1.05, len)) * step(1e-4, r);
}

// 0..1 cloud density at a world XZ position (1 = under a cloud).
float CloudDensity(float2 wp)
{
    float cover = saturate(_CloudCover);
    float2 q = (wp + _CloudShadowShift.xy) * _CloudScale + _CloudOffset.xy;
    float2 b = floor(q - 0.5);
    float dens = max(max(DriftClumpDensity(q, b, cover), DriftClumpDensity(q, b + float2(1, 0), cover)),
                     max(DriftClumpDensity(q, b + float2(0, 1), cover), DriftClumpDensity(q, b + float2(1, 1), cover)));
    return dens * step(0.001, cover);
}

// Multiplier for lit colour: 1 in the open, (1 - _CloudShadowStrength) under a cloud.
float CloudShadow(float2 wp)
{
    return 1.0 - CloudDensity(wp) * _CloudShadowStrength;
}

#endif
