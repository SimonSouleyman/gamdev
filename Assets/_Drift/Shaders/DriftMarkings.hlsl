#ifndef DRIFT_MARKINGS_INCLUDED
#define DRIFT_MARKINGS_INCLUDED

// Procedural coat markings for Drift/Animal and Drift/Critter (Drift.Life.Markings bakes the channel). Vertex UV3 =
//   (u, v, w, code): u, v = pattern coordinates in template units (along / around a body part, x / z on shells and
//   wings, 0..1 along the part for tips), w = 0 belly .. 1 back (countershading),
//   code = pattern * 8 + level (strength 0..7) + seed * 0.9 (per individual).
// The coordinates ride on the template, so a pattern stays on the body through hops, pose blends and the curved
// world. Every pattern fades to its average where its features shrink below ~2 pixels (the footprint is taken
// once, outside the branches), so a herd under the chase camera keeps its tone but does not shimmer; zebras
// fall back to broad bands first. No textures: ~10-40 ALU for fur, ~9 hash cells for patches / scutes / wool.
//
// USE: vertex    DriftMarkDecode(IN.mark.w, id, strength, seed);  OUT.mark = float4(IN.mark.xyz, strength);
//                OUT.markId = float2(id, seed);                    (markId nointerpolation)
//      fragment  col = DriftMarkings(col, IN.mark, IN.markId.x, IN.markId.y, _Markings);

#define MK_NONE 0
#define MK_FUR 1
#define MK_FLUFF 2
#define MK_WOOL 3
#define MK_SKIN 4
#define MK_ZEBRA 5
#define MK_GIRAFFE 6
#define MK_SCUTES 7
#define MK_HIDE 8
#define MK_MEERKAT 9
#define MK_FEATHER 10
#define MK_TIP 11
#define MK_SPECKLE 12
#define MK_WING_YELLOW 13
#define MK_WING_WHITE 14
#define MK_WING_BLUE 15
#define MK_COARSE 16

void DriftMarkDecode(float code, out float id, out float strength, out float seed)
{
    float fl = floor(code + 1e-3);
    seed = saturate((code - fl) / 0.9);
    id = floor(fl * 0.125 + 1e-3);
    strength = (fl - id * 8.0) / 7.0;
}

float MkHash(float2 p)
{
    float3 p3 = frac(p.xyx * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float2 MkHash2(float2 p)
{
    float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float MkNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = MkHash(i), b = MkHash(i + float2(1, 0)), c = MkHash(i + float2(0, 1)), d = MkHash(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// x = distance to the nearest jittered cell point, y = to the second nearest, z = random value of the nearest cell.
float3 MkCells(float2 p, float jitter)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float d1 = 8.0, d2 = 8.0, id = 0.0;
    [unroll] for (int y = -1; y <= 1; y++)
    {
        [unroll] for (int x = -1; x <= 1; x++)
        {
            float2 g = float2(x, y);
            float2 o = MkHash2(i + g);
            float2 r = g + 0.5 + (o - 0.5) * jitter - f;
            float d = dot(r, r);
            if (d < d1) { d2 = d1; d1 = d; id = o.x; }
            else d2 = min(d2, d);
        }
    }
    return float3(sqrt(d1), sqrt(d2), id);
}

// Contrast a pattern with `freq` periods per template unit keeps at footprint fw (template units per pixel):
// 1 with ~4+ pixels per period, 0 below ~1.5.
float MkFade(float fw, float freq)
{
    return saturate((0.7 - fw * freq) * 2.2);
}

// 1 inside a band of `width` (fraction of the period) around every integer of s; fw = footprint of s.
float MkBand(float s, float width, float fw)
{
    float d = abs(frac(s + 0.5) - 0.5) * 2.0;
    float aa = max(fw * 2.0, 1e-4);
    return 1.0 - smoothstep(width - aa, width + aa, d);
}

float MkInside(float d, float r, float aa)
{
    return 1.0 - smoothstep(r - aa, r + aa, d);
}

float MkGrain(float2 uv, float2 freq, float fw, float amp, float2 off)
{
    return (MkNoise(uv * freq + off) - 0.5) * amp * MkFade(fw, max(freq.x, freq.y));
}

float3 MkWing(float3 c, float2 uv, float fw, int p)
{
    float2 q = float2(abs(uv.x), uv.y);
    float aa = fw * 1.5 + 0.004;
    // Wing triangle in template units: root (0, 0.12), front tip (0.5, 0.4), rear tip (0.45, -0.35).
    float dOut = dot(float2(0.5, 0.4) - q, float2(0.9978, -0.0665));
    float dFront = dot(q - float2(0.0, 0.12), float2(0.488, -0.872));
    float dRoot = length(q - float2(0.0, 0.12));
    float3 black = float3(0.006, 0.005, 0.006);
    float3 o = c;
    if (p == MK_WING_YELLOW)
    {
        float ang = atan2(q.y - 0.12, q.x + 0.02);
        float vein = MkBand(ang * 2.4, 0.12, fw * 2.4 / max(dRoot, 0.05) + 0.02) * smoothstep(0.08, 0.16, dRoot);
        float dark = max(max(MkInside(dOut, 0.085, aa), MkInside(dFront, 0.03, aa)), vein);
        o = lerp(o, black, dark);
        float t = dot(q - float2(0.5, 0.4), float2(-0.0665, -0.9978));
        float2 dotP = float2((frac(t * 9.0) - 0.5) / 9.0, dOut - 0.045);
        o = lerp(o, c, MkInside(length(dotP), 0.017, aa) * step(0.03, t));
        float e = length(q - float2(0.32, -0.19));
        o = lerp(o, float3(1.0, 0.25, 0.02), MkInside(e, 0.05, aa));
        o = lerp(o, float3(0.03, 0.1, 0.8), MkInside(e, 0.026, aa));
    }
    else if (p == MK_WING_WHITE)
    {
        o *= lerp(0.55, 1.0, smoothstep(0.02, 0.14, dRoot));
        float dark = max(MkInside(length(q - float2(0.5, 0.4)), 0.19, aa),
                     max(MkInside(length(q - float2(0.27, 0.1)), 0.042, aa), MkInside(length(q - float2(0.21, -0.08)), 0.036, aa)));
        o = lerp(o, float3(0.03, 0.03, 0.035), dark);
    }
    else
    {
        o *= lerp(0.45, 1.0, smoothstep(0.02, 0.2, dRoot));
        o = lerp(o, float3(0.004, 0.006, 0.02), MkInside(dOut, 0.07, aa));
        o = lerp(o, float3(0.9, 0.92, 0.95), MkInside(dOut, 0.016, aa));
        float e = length(q - float2(0.33, -0.2));
        o = lerp(o, float3(0.9, 0.9, 0.85), MkInside(e, 0.042, aa));
        o = lerp(o, black, MkInside(e, 0.028, aa));
    }
    return o;
}

// c: vertex colour (linear); mark = (u, v, w, strength); id, seed from DriftMarkDecode; amount: material switch.
float3 DriftMarkings(float3 c, float4 mark, float id, float seed, float amount)
{
    // Footprint of the pattern coordinates, taken before any branch (derivatives are undefined in divergent flow).
    float2 fw2 = fwidth(mark.xy);
    float fw = max(fw2.x, fw2.y);
    int p = (int)(id + 0.5);
    float3 result = c;
    [branch] if (p > MK_NONE && amount > 0.0)
    {
        float2 uv = mark.xy;
        float w = mark.z;
        float s = mark.w;
        float2 off = float2(seed * 37.1, seed * 17.3);
        float h1 = frac(seed * 13.37 + 0.21), h2 = frac(seed * 7.13 + 0.57);
        // A touch brighter or darker, warmer or cooler per individual.
        float3 indiv = (1.0 + (h1 - 0.5) * 0.16) * float3(1.0 + (h2 - 0.5) * 0.1, 1.0, 1.0 - (h2 - 0.5) * 0.1);
        float mottle = (MkNoise(uv * 4.0 + off) - 0.5) * 0.16 * MkFade(fw, 4.0);
        float3 o = c;

        [branch] if (p == MK_TIP)
        {
            float start = 1.0 - s * 0.7;
            float aa = fw2.x + 0.03;
            o = lerp(c, c * 0.08 + 0.003, smoothstep(start - aa, start + aa, uv.x));
        }
        else if (p >= MK_WING_YELLOW && p <= MK_WING_BLUE)
        {
            o = lerp(c, MkWing(c, uv, fw, p), MkFade(fw, 6.0) * s);
        }
        else if (p == MK_ZEBRA)
        {
            const float k1 = 12.0, k0 = 4.5;
            float sFine = uv.x * k1 + sin(uv.y * 10.0 + seed * 6.28) * 0.35 + seed * 3.0;
            float fine = MkBand(sFine, 0.42, fw * k1 * 1.3);
            float coarse = MkBand(uv.x * k0 + seed, 0.42, fw * k0);
            float m = lerp(lerp(0.42, coarse, MkFade(fw, k0)), fine, MkFade(fw, k1 * 1.3));
            o = c * indiv * (1.0 + mottle * 0.5);
            o = lerp(o, c * 0.03, m * s);
        }
        else if (p == MK_GIRAFFE)
        {
            const float k = 11.0;
            float3 cell = MkCells(uv * k + off, 0.85);
            float f = MkFade(fw, k);
            float aa = fw * k * 1.5;
            float patch = smoothstep(0.1 - aa, 0.1 + aa, cell.y - cell.x);
            float m = lerp(0.72, patch, f);
            float3 spot = c * float3(0.38, 0.19, 0.1) * lerp(1.0, 0.8 + 0.4 * cell.z, f);
            o = lerp(c * indiv * (1.0 + mottle * 0.5), spot, m * s);
        }
        else if (p == MK_SCUTES)
        {
            const float k = 9.0;
            float3 cell = MkCells(uv * k + seed * 5.0, 0.45);
            float f = MkFade(fw, k);
            float aa = fw * k * 1.5;
            float seam = 1.0 - smoothstep(0.07 - aa, 0.07 + aa, cell.y - cell.x);
            float tone = lerp(1.0, (0.88 + 0.28 * cell.z) * (1.2 - 0.45 * cell.x), f * s);
            o = c * indiv * tone * lerp(0.92, 1.06, w);
            o = lerp(o, o * 0.28, seam * f * s);
        }
        else if (p == MK_WOOL)
        {
            const float k = 26.0;
            float3 cell = MkCells(uv * k + off, 1.0);
            float bump = 1.0 - saturate(cell.x * 1.5);
            o = c * indiv * (1.0 + mottle) * lerp(1.0, lerp(0.78, 1.12, bump), MkFade(fw, k) * s) * lerp(0.94, 1.05, w);
        }
        else if (p == MK_SPECKLE)
        {
            const float k = 22.0;
            float3 cell = MkCells(uv * k + off, 0.9);
            float f = MkFade(fw, k);
            float aa = fw * k * 1.5;
            float dotMask = MkInside(cell.x, 0.09 + 0.08 * cell.z, aa) * step(0.5, cell.z);
            o = c * indiv * (1.0 + mottle) * lerp(1.08, 0.9, w);
            o = lerp(o, o * 1.6 + 0.03, dotMask * f * s);
        }
        else if (p == MK_FEATHER)
        {
            o = c * indiv * (1.0 + MkGrain(uv, 40.0, fw, 0.1, off) * s + mottle * 0.5);
            float lum = dot(o, float3(0.3, 0.6, 0.1));
            o = max(lerp(lum.xxx, o, 1.0 + 0.4 * w * s) * lerp(1.06, 0.88, w * s), 0.0);
        }
        else
        {
            // Fur family: grain + mottling + countershading (dark back, pale belly).
            float grain = 0.0, shade = 0.0;
            if (p == MK_COARSE) { grain = MkGrain(uv, float2(16.0, 60.0), fw, 0.26, off); shade = 0.09; }
            else if (p == MK_FLUFF) { grain = MkGrain(uv, 24.0, fw, 0.12, off) + MkGrain(uv, 55.0, fw, 0.07, off.yx); shade = -0.04; }
            else if (p == MK_SKIN) { grain = MkGrain(uv, 70.0, fw, 0.12, off); shade = 0.0; }
            else { grain = MkGrain(uv, 45.0, fw, 0.18, off); shade = 0.1; }
            o = c * indiv * (1.0 + (grain + mottle) * s) * lerp(1.0 + shade, 1.0 - shade, w * s);
            if (p == MK_FLUFF) o *= lerp(float3(0.9, 0.95, 1.0), 1.0, saturate(w * 1.5));
            if (p == MK_HIDE)
            {
                float n = MkNoise(uv * 6.0 + off) * 0.65 + MkNoise(uv * 14.0 + off.yx) * 0.35;
                float patchy = step(0.45, frac(seed * 3.71 + 0.3));
                float blotch = smoothstep(0.57, 0.63, n) * MkFade(fw, 10.0) * patchy;
                o = lerp(o, o * 1.9 + 0.02, blotch * 0.85 * s);
            }
            else if (p == MK_MEERKAT)
            {
                const float k = 16.0;
                float band = MkBand(uv.x * k + MkNoise(uv * 8.0 + off) * 0.5, 0.3, fw * k * 1.2) * smoothstep(0.55, 0.8, w);
                o = lerp(o, o * 0.4, band * MkFade(fw, k) * s);
            }
        }
        result = lerp(c, o, amount);
    }
    return result;
}

#endif
