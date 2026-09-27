#ifndef DRIFT_WATER_INCLUDED
#define DRIFT_WATER_INCLUDED

// Everything Drift/Water and Drift/WaterFlecks share: the feedback uniforms Drift.Visuals.WaterFeedback pushes to both
// renderers through one MaterialPropertyBlock, the player's coast distance and the storm field.
// Include after DriftClouds.hlsl (DriftNoise).

// Per-frame feedback uniforms, pushed by Drift.Visuals.WaterFeedback through a
// MaterialPropertyBlock. Deliberately not material Properties so the .mat asset never
// serializes them and everything below degrades to nothing when they are left at zero.
float4 _PlayerPos;      // xy = player island world xz
float4 _PlayerVel;      // xy = player planar velocity
// Island outline for the wake, all pushed by WaterFeedback:
//   _PlayerWakeEdge = widest outline points across the velocity: (along, across) of the +perp side, then of the -perp side
//   _PlayerWake     = (churn reach from the shore, V line length, cutoff range from the island centre [0 = no wake], foam band scale)
float4 _PlayerWakeEdge;
float4 _PlayerWake;
float  _PlayerWakeDamp;  // share of the churn foam taken away behind a very wide island (0 = small island, as ever)
float4 _RingPos;        // xy = ring 0 centre, zw = ring 1 centre
float4 _RingAge;        // x, y = ring ages in seconds
float4 _RingStrength;   // x, y = ring strengths (0 = inactive)
float4 _CurrentDir;     // xy = normalized plate current direction
float  _CurrentSpeed;
float  _CurrentFoam;    // current fleck strength knob (WaterFeedback.currentStreakStrength)
// Plate currents around the view (Drift.Visuals.CurrentField): rg = velocity / max * 0.5 + 0.5,
// b = closeness to a plate boundary, a = closing speed there. Params: xy = world origin, z = 1 / size,
// w = max speed (0 = no field: no flecks).
TEXTURE2D(_CurrentField);
SAMPLER(sampler_CurrentField);
float4 _CurrentFieldParams;
float4 _CurrentEmphasis;  // x = distance from the coastline, y = extra strength (carry), z = carry against steering
// Drift.Islands.SpeedFeel through WaterFeedback: x = drive (0 = idling, 1 = own top speed, up to 1.4 when
// the water carries), y = flow build-up while travelling with the current, z = surf on a plate boundary,
// w = the island's speed in u/s. Gains: x = speed lines, y = bow/wake boost, z = flow, w = surf. All zero
// (the default and "Tempogefühl aus") leaves the water exactly as it was.
float4 _SpeedFeel;
float4 _SpeedFeelGains;
float4 _SprayPos;       // xy = merge contact, z = age, w = strength
// How much light the shallow tint and the foam get (Drift.Visuals.DayNightCycle through WaterFeedback): rgb =
// multiplier, 1 by day, the dim bluish moonlight at night; w = 0 when nobody set it (then 1). Without it the
// fixed turquoise and white glowed at night as bright slabs along every shore.
float4 _WaterLight;
float  _Storm;          // 0..1 storm intensity at the player
// Every visible storm (Drift.Visuals.StormVisuals): xy centre, z radius, w intensity. Unset = no storms.
float4 _DriftStorms[4];
float  _DriftStormCount;
float4 _WindDir;        // xy = normalized wind (current + global wind), z = wind speed
float4 _SplashPos;      // xy = fish splash centre, z = age, w = strength
// The sky's moon (Drift.Visuals.CurvedWorld, per camera; see DriftSky.hlsl): xyz = where it is drawn,
// w = 1 / disc radius (chord); colour a = visibility; stars x = how dark the sky is (0 by day); star
// params y = halo strength x lit share of the disc.
float4 _SkyMoonDir;
float4 _SkyMoonColor;
float4 _SkyStars;
float4 _SkyStarParams;
// The player island's real coastline (Drift.Visuals.CoastField): a small signed-distance field in
// the island's BODY space. r = distance to the waterline in world units (negative on land),
// gb = outward normal there. Everything the water draws around the player reads this - a bounding
// circle or the radial profile that came before fills the notches of a branched island and turns
// the wake into a white box with straight edges.
TEXTURE2D(_CoastField);
SAMPLER(sampler_CoastField);
float4 _CoastParams;    // xy = field origin in body space, z = 1 / size, w = 1 when the field is valid
float4 _CoastRot;       // xy = (cos, sin) of the island's yaw (world -> body)
float  _CurveSkyDraw;   // Drift.Visuals.CurvedWorld: 1 when this camera draws the sky dome after the water
float4 _CurveSkyZenith; // the dome's gradient (DriftSky.hlsl), for the sky under the sea where it shows
float4 _CurveSkyCenter;

// Distance to the player island's shore with its outward normal (world space). `fade` goes to 0 at
// the field's own border, so nothing anchored to it can ever draw a straight edge there.
float CoastDistance(float2 wp, out float2 nrm, out float fade)
{
    nrm = float2(0.0, 1.0);
    fade = 0.0;
    if (_CoastParams.w <= 0.0) return 1e4;
    float2 rel = wp - _PlayerPos.xy;
    float2 lp = float2(rel.x * _CoastRot.x - rel.y * _CoastRot.y, rel.x * _CoastRot.y + rel.y * _CoastRot.x);
    float2 uv = (lp - _CoastParams.xy) * _CoastParams.z;
    float2 e = saturate(min(uv, 1.0 - uv) * 14.0);
    fade = e.x * e.y;
    if (fade <= 0.0) return 1e4;
    float4 s = SAMPLE_TEXTURE2D_LOD(_CoastField, sampler_CoastField, uv, 0);
    float2 nb = s.gb;
    nb = dot(nb, nb) > 1e-6 ? normalize(nb) : float2(0.0, 1.0);
    nrm = float2(nb.x * _CoastRot.x + nb.y * _CoastRot.y, -nb.x * _CoastRot.y + nb.y * _CoastRot.x);
    return s.r;
}

// The profile of StormSystem.IntensityAt (full strength over the inner 55 %, easing out to the rim) with
// its rim pushed in and out by noise, so the rough sea is a ragged patch under the cloud cluster, not a disc.
float StormField(float2 wp)
{
    float storm = 0.0;
    for (int i = 0; i < 4; i++)
    {
        if (i >= (int)_DriftStormCount) break;
        float4 s = _DriftStorms[i];
        float d = length(wp - s.xy) / max(s.z, 1e-3);
        // The noise scales d by 0.82..1.18, so inside 0.55 / 1.18 the storm is full and past 1 / 0.82 it is gone
        // whatever the noise says: only the ragged rim pays for it. Exactly the same field.
        float k = d < 0.466 ? 1.0 : 0.0;
        [branch] if (d >= 0.466 && d < 1.2196)
        {
            d *= 0.82 + 0.36 * DriftNoise(wp * 0.05 + s.xy * 0.013);
            k = 1.0 - smoothstep(0.55, 1.0, d);
        }
        storm = max(storm, s.w * k);
    }
    return storm;
}

#endif
