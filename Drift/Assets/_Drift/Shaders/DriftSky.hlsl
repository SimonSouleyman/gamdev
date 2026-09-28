#ifndef DRIFT_SKY_INCLUDED
#define DRIFT_SKY_INCLUDED

// The procedural day / night sky shared by Drift/CurvedSky (the dome of the curved world) and Drift/Skybox (the
// RenderSettings fallback). Include after URP Core/Lighting, DriftCurve.hlsl and DriftClouds.hlsl.
// Every colour is pushed by Drift.Visuals.CurvedWorld from the palette DayNightCycle evaluated this frame (already
// linear); the colour at the limb is exactly _CurveFogColor, the one the sea and far islands fade into, and every
// directional effect (sun tint, violet belt, clouds, stars) starts above the limb, so the seam stays invisible.
// Cost per pixel above the limb: two value-noise lookups (with analytic gradient) for the clouds; at night (uniform
// branch) one 3D cell hash for the stars and, inside the Milky Way band only, two more noise lookups. No textures,
// no pow / exp; one sin for the twinkle and two inside the moon's disc. Below the limb: one dot product.
float4 _CurveSkyZenith;   // rgb = zenith colour
float4 _CurveSkyCenter;   // xyz = direction from the camera to the planet's centre, w = cos of its angular radius
float4 _SkySunDir;        // xyz = where the sun is drawn (per camera: elevation counts from the limb), w = chord^2 of the disc radius
float4 _SkySunDisc;       // rgb = disc colour, a = visibility
float4 _SkySunGlow;       // rgb = warm tint on the sun's side, a = strength
float4 _SkyAntiGlow;      // rgb = violet band opposite the sun, a = strength
float4 _SkyMoonDir;       // xyz, w = 1 / disc radius (chord)
float4 _SkyMoonRight;     // xyz = disc x axis, w = phase light x
float4 _SkyMoonUp;        // xyz = disc y axis, w = phase light z
float4 _SkyMoonColor;     // rgb, a = visibility
float4 _SkyStars;         // x = visibility, y = cells per unit direction, z = twinkle speed, w = Milky Way strength
float4 _SkyStarParams;    // x = star radius in cells, y = moon halo strength, z = sun halo strength, w = gradient steepness
float4 _SkyStarRot0;      // world direction -> star space (the field turns with the time of day)
float4 _SkyStarRot1;
float4 _SkyStarRot2;
float4 _SkyCloudLit;      // rgb, a = cover scale
float4 _SkyCloudDark;     // rgb, a = layer height above the camera
float4 _SkyShootHead;     // xyz, w = intensity (0 = none)
float4 _SkyShootTail;     // xyz, w = width (chord)
float4 _SkyCloudLight;    // xy = horizontal direction towards the sun / moon, z = strength of the directional shading, w = horizon softening

// Rare night spectacles, pushed by Drift.Visuals.WorldEvents and left at zero the rest of the time: with
// _SkyAurora.x and _SkyMeteorParams.x at 0 (their default) both branches below are skipped and the sky is exactly
// the sky described above.
float4 _SkyAurora;        // x = strength (0 = none), y = foot of the curtain above the limb, z = its height, w = drift speed
float4 _SkyAuroraDir;     // xyz = horizontal direction towards the middle of the curtain
float4 _SkyAuroraRight;   // xyz = across it
float4 _SkyAuroraLow;     // rgb = colour at the foot
float4 _SkyAuroraHigh;    // rgb = colour at the top
float4 _SkyMeteorParams;  // x = brightness (0 = no shower), y = head width (chord), z = travel / tail length (chord)
#define DRIFT_SKY_METEORS 3
float4 _SkyMeteors[DRIFT_SKY_METEORS];     // xyz = where it lit up, w = life left (1 -> 0, 0 = slot empty)
float4 _SkyMeteorDirs[DRIFT_SKY_METEORS];  // xyz = away from the radiant, w = length factor

// Adventure ring only (Drift.Visuals.CurvedWorld, zero everywhere else): past the rims of the band the world ends
// and there is nothing but the starfield. x = how much of it (0 = off), y = star brightness out there by day,
// z = soft edge at the rim in world units, w = how much of the Milky Way is left out there.
// _SkySpaceColor: rgb = the space between the stars (linear), a = how much of the sun / moon disc is still drawn.
float4 _SkySpace;
float4 _SkySpaceColor;

float3 DriftSkyStarSpace(float3 dir)
{
    return float3(dot(_SkyStarRot0.xyz, dir), dot(_SkyStarRot1.xyz, dir), dot(_SkyStarRot2.xyz, dir));
}

float3 DriftSkyHash33(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.xxy + p.yxx) * p.zyx);
}

// DriftNoise with its gradient (same lattice and hash, so it is the very field of the cloud shadows): the
// gradient is what shades the clouds towards the light without a second lookup.
float3 DriftSkyNoiseD(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float2 du = 6.0 * f * (1.0 - f);
    float a = DriftHash(i);
    float b = DriftHash(i + float2(1, 0));
    float c = DriftHash(i + float2(0, 1));
    float d = DriftHash(i + float2(1, 1));
    float k = a - b - c + d;
    return float3(lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y), du * float2(b - a + k * u.y, c - a + k * u.x));
}

// The star field itself: one 3D cell hash for the stars, the Milky Way band (two more noise lookups inside it only)
// and the single shooting star. `vis` is how dark the sky is (it only gates the Milky Way and the dust; the caller
// scales the whole result). Shared by the sky above the limb and by the space outside the adventure ring.
float3 DriftSkyStarField(float3 dir, float3 sdir, float vis)
{
    float3 p = sdir * _SkyStars.y;
    float3 id = floor(p);
    float3 h = DriftSkyHash33(id);
    float3 dd = p - id - (0.25 + 0.5 * h);
    float r2 = _SkyStarParams.x * _SkyStarParams.x;
    float star = saturate(1.0 - dot(dd, dd) / r2);
    float hb = frac(h.x * 37.7 + h.y * 17.3);
    float bright = 0.35 + 2.4 * hb * hb * hb;
    float twinkle = 0.78 + 0.22 * sin(_Time.y * _SkyStars.z * (0.6 + h.y) + h.x * 40.0);
    float3 tint = lerp(float3(0.72, 0.84, 1.0), float3(1.0, 0.86, 0.68), h.z * h.z);
    float3 stars = tint * (star * star * bright * twinkle);

    // Milky Way: a soft band along a fixed great circle of the star field, broken up by two octaves of noise.
    const float3 mwN = float3(0.35, 0.52, -0.779);
    const float3 mwA = float3(0.912, 0.0, 0.41);
    const float3 mwB = float3(0.213, -0.854, -0.474);
    float across = dot(sdir, mwN);
    float band = 1.0 / (1.0 + across * across * 10.0);
    band = saturate(band * band * 1.12 - 0.12);
    UNITY_BRANCH
    if (band * _SkyStars.w > 0.0)
    {
        float2 uv = float2(dot(sdir, mwA), dot(sdir, mwB)) * ((1.0 + across * 1.2) * 7.0);
        float m = DriftNoise(uv) * 0.6 + DriftNoise(uv * 2.6 + 7.1) * 0.4;
        float dust = smoothstep(0.3, 0.8, m);
        // Needs a dark sky: fades in with the square of the star visibility.
        stars += float3(0.5, 0.58, 0.85) * (band * (0.3 + 0.7 * dust) * _SkyStars.w * vis * 0.16);
        stars *= 1.0 + band * dust * 0.6;
    }

    float3 sv = dir - _SkyShootHead.xyz;
    float3 seg = _SkyShootTail.xyz - _SkyShootHead.xyz;
    float along = saturate(dot(sv, seg) / max(dot(seg, seg), 1e-8));
    float3 off = sv - seg * along;
    float streak = saturate(1.0 - dot(off, off) / (_SkyShootTail.w * _SkyShootTail.w));
    stars += float3(1.0, 0.95, 0.85) * (streak * streak * (1.0 - along) * (1.0 - along) * _SkyShootHead.w * 2.0);
    return stars;
}

// A handful of streaks radiating from one point, each running away from it over its short life. Unscaled.
float3 DriftSkyMeteorStreaks(float3 dir)
{
    float3 shower = 0.0;
    float len = _SkyMeteorParams.z;
    for (int mi = 0; mi < DRIFT_SKY_METEORS; mi++)
    {
        float4 m = _SkyMeteors[mi];
        if (m.w <= 0.0) continue;
        float4 md = _SkyMeteorDirs[mi];
        float3 head = normalize(m.xyz + md.xyz * (len * md.w * (1.0 - m.w)));
        float3 seg = -md.xyz * (len * md.w * (0.35 + 0.65 * m.w));
        float3 sv = dir - head;
        float along = saturate(dot(sv, seg) / max(dot(seg, seg), 1e-8));
        float3 off = sv - seg * along;
        float streak = saturate(1.0 - dot(off, off) / (_SkyMeteorParams.y * _SkyMeteorParams.y));
        // The head is a bright point, the tail keeps a long soft falloff behind it.
        shower += float3(1.0, 0.95, 0.82) * (streak * streak * (1.0 - along) * (0.35 + 0.65 * (1.0 - along)) * m.w * m.w);
    }
    return shower;
}

// The moon's disc (phase terminator, maria, limb darkening). mask = 0 outside it.
float3 DriftSkyMoonDisc(float3 moonV, float moonR2, out float mask)
{
    float mx = dot(moonV, _SkyMoonRight.xyz) * _SkyMoonDir.w;
    float my = dot(moonV, _SkyMoonUp.xyz) * _SkyMoonDir.w;
    float rr = mx * mx + my * my;
    float mz = sqrt(saturate(1.0 - rr));
    float lit = smoothstep(-0.08, 0.22, mx * _SkyMoonRight.w + mz * _SkyMoonUp.w);
    float maria = 1.0 - 0.16 * saturate(sin(mx * 4.1 + my * 2.3 + 1.0) * sin(my * 5.3 - mx * 1.7) + 0.35);
    mask = 1.0 - smoothstep(0.86, 1.0, rr);
    return _SkyMoonColor.rgb * ((0.06 + 0.94 * lit) * maria * (0.75 + 0.25 * mz));
}

// Interleaved gradient noise about one display step wide (the target is linear, so the step grows with the
// brightness): the dark gradients band otherwise.
float3 DriftSkyDither(float3 col, float2 pixel)
{
    float ign = frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
    return col + (ign - 0.5) * (sqrt(max(col.g, 0.0004)) * 0.009);
}

// up = how far the direction is above the limb (> 0).
float3 DriftSkyAbove(float3 dir, float3 sdir, float3 camPos, float2 pixel, float up)
{
    float q = 1.0 / (1.0 + _SkyStarParams.w * up);
    float g = 1.0 - q * q;
    float3 col = lerp(_CurveFogColor.rgb, _CurveSkyZenith.rgb, g);

    // Directional tints fade in just above the limb: the limb itself keeps the haze colour all round.
    float rise = saturate(up * 12.0);
    float low = rise * q * q;
    float3 toSun = dir - _SkySunDir.xyz;
    float sunD2 = dot(toSun, toSun);
    float s01 = 1.0 - sunD2 * 0.25;
    float s2 = s01 * s01;
    float s4 = s2 * s2;
    col = lerp(col, _SkySunGlow.rgb, saturate(_SkySunGlow.a * (s4 * low * 0.9 + s4 * s4 * s4 * 0.25 * rise)));
    float anti = 1.0 - s01;
    float b = up * 9.0;
    float belt = 2.0 * b / (1.0 + b * b);
    col = lerp(col, _SkyAntiGlow.rgb, saturate(_SkyAntiGlow.a * anti * anti * belt * 0.85));

    // Cloud layer: the very field CloudShadows drifts over the sea (DriftClouds.hlsl), seen from below on a
    // layer above the camera, so the shadows on the water belong to the clouds in the sky. Evaluated first
    // because it hides the stars; composited last.
    float cover = saturate(_CloudCover * _SkyCloudLit.a);
    float dens = 0.0;
    float shade = 0.0;
    UNITY_BRANCH
    if (cover > 0.01)
    {
        float2 cp = (camPos.xz + dir.xz * (_SkyCloudDark.a / (up + _SkyCloudLight.w))) * _CloudScale + _CloudOffset.xy;
        float3 n1 = DriftSkyNoiseD(cp);
        float3 n2 = DriftSkyNoiseD(cp * 2.7 + 17.3);
        float c = n1.x * 0.62 + n2.x * 0.38;
        float2 grad = n1.yz * 0.62 + n2.yz * (0.38 * 2.7);
        float lo = 0.9 - cover;
        dens = smoothstep(lo, lo + 0.18, c) * saturate((up - 0.012) * 9.0);
        // Density falling towards the light = the lit flank; the thick middle of a cloud is its grey belly. Under
        // a closed cover the flank term fades (it would draw the noise lattice) and the bellies carry the relief.
        float open = saturate(2.2 - 2.4 * cover);
        shade = saturate(0.42 + dot(grad, _SkyCloudLight.xy) * (_SkyCloudLight.z * open) + (0.75 - 0.47 * open) * smoothstep(lo + 0.1, lo + 0.75, c) - 0.08 * open);
    }
    float clear = (1.0 - dens) * (1.0 - dens);

    float nightVis = _SkyStars.x * clear;
    float3 moonV = dir - _SkyMoonDir.xyz;
    float moonD2 = dot(moonV, moonV);
    float moonR2 = 1.0 / (_SkyMoonDir.w * _SkyMoonDir.w);
    float limbFade = saturate(up * 6.0);

    UNITY_BRANCH
    if (_SkyStars.x > 0.004)
    {
        float moonHole = saturate(moonD2 / moonR2 - 1.0);
        col += DriftSkyStarField(dir, sdir, _SkyStars.x) * (nightVis * limbFade * moonHole);
    }

    // Northern lights: a curtain standing in one compass direction, its foot waving over the sky and broken into
    // fine vertical rays. Two noise lookups for the folds, one for the rays; nothing at all while it is off.
    UNITY_BRANCH
    if (_SkyAurora.x > 0.001)
    {
        float f = dot(dir, _SkyAuroraDir.xyz);
        float sd = dot(dir, _SkyAuroraRight.xyz);
        float hlen = max(sqrt(f * f + sd * sd), 1e-4);
        float face = saturate(1.25 * (f / hlen) - 0.15);
        float azi = sd / hlen;
        float t = _Time.y * _SkyAurora.w;
        // The foot of the curtain folds slowly over the sky; two octaves so it kinks instead of waving.
        float fold = (DriftNoise(float2(azi * 2.6 + t * 0.35, t * 0.11)) - 0.5) * 0.7
                   + (DriftNoise(float2(azi * 7.3 - t * 0.21, 3.7 + t * 0.07)) - 0.5) * 0.3;
        float v = (up - (_SkyAurora.y + fold * _SkyAurora.z)) / _SkyAurora.z;
        UNITY_BRANCH
        if (v > 0.0 && v < 1.0 && face > 0.0)
        {
            // Rays hang down the curtain: fine stripes across the azimuth (almost no dependence on the height,
            // or they break into blobs) over a broad brightness that drifts along it.
            float fine = DriftNoise(float2(azi * 96.0 + t * 0.5, v * 0.25));
            float broad = DriftNoise(float2(azi * 21.0 - t * 0.3, 2.1 + v * 0.15));
            float rays = saturate(0.3 + 0.8 * fine) * (0.35 + 0.95 * broad);
            // Brightest along the bottom edge, thinning out towards the top.
            float glow = saturate(v * 13.0) * (1.0 - v) * (1.0 - v) * (1.0 - v);
            float3 tint = lerp(_SkyAuroraLow.rgb, _SkyAuroraHigh.rgb, saturate(v * 1.5));
            col += tint * (_SkyAurora.x * 0.55 * _SkyStars.x * face * rays * glow * limbFade);
        }
    }

    // Meteor shower: a handful of streaks radiating from one point, each running away from it over its short life.
    UNITY_BRANCH
    if (_SkyMeteorParams.x > 0.001)
        col += DriftSkyMeteorStreaks(dir) * (_SkyMeteorParams.x * nightVis * limbFade * 3.0);

    // Halos first, so clouds in front of the sun or the moon are what catches the light.
    float sunHalo = _SkySunDir.w * 2.5 / (sunD2 + _SkySunDir.w * 2.5);
    float moonHalo = moonR2 / (moonD2 + moonR2);
    float3 halo = _SkySunDisc.rgb * (_SkySunDisc.a * _SkyStarParams.z * (sunHalo * 0.7 + s4 * s4 * s4 * s4 * 0.1))
                + _SkyMoonColor.rgb * (_SkyMoonColor.a * _SkyStarParams.y * (moonHalo * 0.4 + moonHalo * moonHalo * 0.5));
    col += halo * limbFade;

    // Nothing of the sun or the moon below the limb: the sea is drawn over the dome but is not fully opaque.
    float above = saturate(up * 60.0);
    float disc = 1.0 - smoothstep(_SkySunDir.w * 0.82, _SkySunDir.w * 1.05, sunD2);
    col = lerp(col, _SkySunDisc.rgb * 1.6, disc * _SkySunDisc.a * above);

    UNITY_BRANCH
    if (moonD2 < moonR2)
    {
        float mask;
        float3 moon = DriftSkyMoonDisc(moonV, moonR2, mask);
        col = lerp(col, max(col, moon), mask * _SkyMoonColor.a * above);
    }

    float3 cloud = lerp(_SkyCloudLit.rgb, _SkyCloudDark.rgb, shade);
    cloud += halo * (1.0 - shade) * 1.5;
    col = lerp(col, cloud, dens * 0.94);

    return DriftSkyDither(col, pixel);
}

// Space outside the adventure ring: the starfield and nothing else - no gradient, no haze, no clouds, no glow.
// The stars stand by day too (_SkySpace.y), the way they do from inside a ring world.
float3 DriftSkySpace(float3 dir, float3 sdir, float2 pixel)
{
    float vis = max(_SkyStars.x, _SkySpace.y);
    float3 moonV = dir - _SkyMoonDir.xyz;
    float moonD2 = dot(moonV, moonV);
    float moonR2 = 1.0 / (_SkyMoonDir.w * _SkyMoonDir.w);
    float moonHole = saturate(moonD2 / moonR2 - 1.0);

    // The Milky Way keeps the night's own visibility and is held back on top of that (_SkySpace.w): against the
    // black of space its dust reads as grey cloud, which is exactly what must not be out here. Only the points of
    // light carry the field.
    float3 col = _SkySpaceColor.rgb;
    col += DriftSkyStarField(dir, sdir, _SkyStars.x * _SkySpace.w) * (vis * moonHole);

    UNITY_BRANCH
    if (_SkyMeteorParams.x > 0.001)
        col += DriftSkyMeteorStreaks(dir) * (_SkyMeteorParams.x * vis * 3.0);

    UNITY_BRANCH
    if (_SkySpaceColor.a > 0.001)
    {
        float3 toSun = dir - _SkySunDir.xyz;
        float sunD2 = dot(toSun, toSun);
        float disc = 1.0 - smoothstep(_SkySunDir.w * 0.82, _SkySunDir.w * 1.05, sunD2);
        col = lerp(col, _SkySunDisc.rgb * 1.6, disc * _SkySunDisc.a * _SkySpaceColor.a);
        UNITY_BRANCH
        if (moonD2 < moonR2)
        {
            float mask;
            float3 moon = DriftSkyMoonDisc(moonV, moonR2, mask);
            col = lerp(col, max(col, moon), mask * _SkyMoonColor.a * _SkySpaceColor.a);
        }
    }
    return DriftSkyDither(col, pixel);
}

// 1 where a view ray leaves the ring through one of its open ends, 0 where it meets the band. The band is the
// inside of a cylinder of radius R about an axis that runs along x, R above the sea at the focus (DriftRingWS);
// from inside, the only way out is past a rim, so this is exactly the silhouette the sea draws.
float DriftSkyOutsideRing(float3 dir, float3 camPos)
{
    float invR = _CurveRing.x;
    if (invR <= 0.0) return 0.0;
    float R = 1.0 / invR;
    float2 o = float2(camPos.y - R, camPos.z - _CurveFocus.y);
    float2 d = dir.yz;
    float a = dot(d, d);
    float b = dot(o, d);
    float c = dot(o, o) - R * R;
    // Looking straight along the ring's axis there is no hit at all: that ray is out of the end for certain.
    float t = a < 1e-8 ? 1e9 : (-b + sqrt(max(b * b - a * c, 0.0))) / a;
    float x = camPos.x + dir.x * t;
    float soft = max(_SkySpace.z, 1e-3);
    return max(smoothstep(-soft, soft, x - (_CurveRing.y + _CurveRing.z)),
               smoothstep(-soft, soft, (_CurveRing.y - _CurveRing.z) - x));
}

float3 DriftSky(float3 dir, float3 sdir, float3 camPos, float2 pixel)
{
    float outside = 0.0;
    UNITY_BRANCH
    if (_SkySpace.x > 0.001) outside = DriftSkyOutsideRing(dir, camPos) * saturate(_SkySpace.x);

    // Below the limb only the haze colour (the dome skips the pixels the sea covers, which blends this very colour
    // under itself). Everything else fades in from zero at up = 0, so there is no edge.
    float up = _CurveSkyCenter.w - dot(dir, _CurveSkyCenter.xyz);
    float3 col = _CurveFogColor.rgb;
    UNITY_BRANCH
    if (up > 0.0 && outside < 0.999) col = DriftSkyAbove(dir, sdir, camPos, pixel, up);
    UNITY_BRANCH
    if (outside > 0.001) col = lerp(col, DriftSkySpace(dir, sdir, pixel), outside);
    return col;
}

#endif
