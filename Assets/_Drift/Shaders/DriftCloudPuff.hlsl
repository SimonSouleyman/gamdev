#ifndef DRIFT_CLOUD_PUFF_INCLUDED
#define DRIFT_CLOUD_PUFF_INCLUDED

// Cloud puffs: camera-facing billboards shaded as soft, noisy spheres. Shared by Drift/Clouds (the fair-weather
// clumps of DriftClouds.hlsl) and Drift/Storm (the dark storm clusters of StormVisuals). Include after URP
// Lighting.hlsl, DriftClouds.hlsl and DriftCurve.hlsl.

// Pushed by Drift.Visuals.CloudShadows.
float4 _CloudFocus;    // xyz = the player's island (world, sea level), w = its radius (0 = no clear view kept)
float  _CloudHeight;   // base height of the cloud layer
// Pushed by Drift.Visuals.StormVisuals: 1 on phones - the finest of the three noise octaves of every puff pixel is
// skipped (a uniform branch; the outline keeps its two big octaves). 0 = full detail.
float  _DriftPuffLite;

#ifndef DRIFT_SKY_INCLUDED
// The palette's cloud colours (pushed by CurvedWorld for the sky, already linear); zero = not pushed.
float4 _SkyCloudLit;
float4 _SkyCloudDark;
#endif

float3 DriftPuffLitColor()
{
    return dot(_SkyCloudLit.rgb, 1.0) > 0.001 ? _SkyCloudLit.rgb : float3(1.0, 1.0, 1.0);
}

float3 DriftPuffDarkColor()
{
    return dot(_SkyCloudDark.rgb, 1.0) > 0.001 ? _SkyCloudDark.rgb : float3(0.62, 0.66, 0.74);
}

// Puff k of a clump (radius r in world units, long axis / stretch as DriftCloudClump): draw order = index order, so
// the six ring puffs come first, then the big centre one, then two tops. Mirrored by Drift.Visuals.CloudField.
void DriftPuffLayout(float k, float seed, float2 axis, float aspect, float r, out float2 offset, out float size, out float height)
{
    float h = DriftHash(float2(seed, k * 7.13 + 1.7));
    float2 side = float2(-axis.y, axis.x);
    if (k < 5.5)
    {
        float h2 = frac(h * 7.31);
        float a = k * 1.0471976 + (h - 0.5) * 0.9;
        float2 l = float2(cos(a) * aspect, sin(a)) * (0.44 + 0.2 * h2);
        offset = (axis * l.x + side * l.y) * r;
        size = (0.28 + 0.24 * frac(h * 13.7)) * r;
        height = (0.02 + 0.16 * h) * r;
    }
    else if (k < 6.5)
    {
        offset = 0.0;
        size = 0.56 * r;
        height = 0.3 * r;
    }
    else
    {
        float s = k < 7.5 ? -1.0 : 1.0;
        offset = axis * (s * (0.22 + 0.1 * h) * aspect * r) + side * ((h - 0.5) * 0.2 * r);
        size = (0.36 + 0.1 * h) * r;
        height = (0.45 + 0.12 * h) * r;
    }
}

// Bent world position of billboard corner (-1..1) of a puff centred at the unbent world point.
float3 DriftPuffCorner(float3 centreWS, float2 corner, float radius)
{
    float3 c = DriftCurveWS(centreWS);
    return c + (UNITY_MATRIX_V[0].xyz * corner.x + UNITY_MATRIX_V[1].xyz * corner.y) * radius;
}

// 0..1 visibility of a puff so it never hides the player's island: puffs fade where they would overlap it on screen
// (in projected units, so it works at every zoom). No depth test on purpose: a puff behind the island's centre still
// floats above its far half. Also fades puffs the camera is about to fly through.
float DriftPuffClearView(float3 centreWS, float radius)
{
    float3 bent = DriftCurveWS(centreWS);
    float camD = length(bent - _WorldSpaceCameraPos);
    float nearFade = saturate((camD - radius * 1.3) / (radius * 1.5 + 2.0));
    if (_CloudFocus.w <= 0.0) return nearFade;
    float3 focusBent = DriftCurveWS(_CloudFocus.xyz);
    float4 fc = TransformWorldToHClip(focusBent);
    float4 pc = TransformWorldToHClip(bent);
    float fw = max(fc.w, 1e-3), pw = max(pc.w, 1e-3);
    float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
    float2 dd = (pc.xy / pw - fc.xy / fw) * float2(aspect, 1.0);
    float k = abs(UNITY_MATRIX_P[1][1]);   // negative when rendering into a flipped target
    // The island is a flat disc (plus its hills and trees): seen from a low camera it is wide but short on screen.
    // A round clear zone of its full radius hid every cloud above it - on the adventure ring that was the storm
    // straight ahead. The zone is an ellipse, as tall as the island looks from this angle.
    float rx = _CloudFocus.w * k / fw;
    float tilt = abs(normalize(focusBent - _WorldSpaceCameraPos).y);
    float ry = min(rx, (_CloudFocus.w * (tilt + 0.35)) * k / fw);
    dd.y *= rx / max(ry, 1e-4);
    float gap = length(dd) - rx - radius * k / pw * 0.6;
    return nearFade * smoothstep(0.02, 0.22, gap);
}

// Shading of a puff at billboard uv (-1..1): a soft sphere lit by the main light (wrap diffuse, grey belly,
// a bright rim towards the sun) with a noisy, slowly boiling outline. rgb = colour, a = coverage (0..1).
float4 DriftPuffShade(float2 uv, float seed, float boil, float3 litCol, float3 darkCol)
{
    float r2 = dot(uv, uv);
    float r = sqrt(r2);
    float2 drift = float2(boil, -0.7 * boil);
    float n1 = DriftNoise(uv * 2.3 + seed * 17.0 + drift);
    float n2 = DriftNoise(uv * 5.3 - seed * 9.0 - drift * 1.6);
    float n3 = 0.5;
    UNITY_BRANCH
    if (_DriftPuffLite < 0.5) n3 = DriftNoise(uv * 11.0 + seed * 5.0 + drift * 2.3);
    float n = n1 * 0.55 + n2 * 0.3 + n3 * 0.15;
    // Cauliflower outline: the bumps of the noise push the rim in and out.
    float edge = 0.66 + 0.34 * n;
    float a = 1.0 - smoothstep(edge - 0.16, edge, r);
    float z = sqrt(saturate(1.0 - r2));
    // The same bumps tilt the normal, so every lobe gets its own lit side and shadow.
    float2 bump = float2(n2 - n1, n3 - n2) * 0.9;
    float3 nWS = normalize(UNITY_MATRIX_V[0].xyz * (uv.x + bump.x) + UNITY_MATRIX_V[1].xyz * (uv.y + bump.y) + UNITY_MATRIX_V[2].xyz * (z + 0.2));
    float3 L = _MainLightPosition.xyz;
    float nl = dot(nWS, L);
    float diff = saturate(nl * 0.6 + 0.45);
    float top = saturate(nWS.y * 0.7 + 0.3);
    float lit = diff * (0.55 + 0.45 * top) * (0.8 + 0.4 * n);
    float3 col = lerp(darkCol * 0.85, litCol, saturate(lit));
    col += litCol * (saturate(nl) * saturate(1.0 - z * 1.4) * 0.3);
    return float4(col, a);
}

#endif
