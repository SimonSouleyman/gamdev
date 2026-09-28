#ifndef DRIFT_CURVE_INCLUDED
#define DRIFT_CURVE_INCLUDED

// Curved world: the simulation is a flat torus, the planet look is a vertex bend. Every world shader wraps
// its world-space vertex onto a sphere of radius R that touches the sea at _CurveFocus (the point the camera
// looks at): a point at planar distance d lands at the arc angle d / R, its height stays along the sphere's
// normal. Near the focus that is the classic drop of d^2 / (2R) (plus a pull-in of d^3 / (6 R^2)), and it is
// exactly the identity at the focus, so gameplay, picking and UI projection near the player stay correct;
// far away the sea closes into a round limb instead of the paraboloid's egg.
//
// HOW TO USE IN A NEW WORLD SHADER (include after the URP Core/Lighting include):
//     #include "DriftCurve.hlsl"
//     vertex:    float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);   // + your own animation
//                OUT.positionHCS = DriftCurveHClip(posWS);                   // bent clip position
//                OUT.positionWS  = posWS;                                    // keep the UNBENT position:
//                                                                            // height colouring, clip planes,
//                                                                            // cloud shadows and noise use it
//     fragment:  col = DriftFog(col, IN.positionWS);                         // optional horizon haze
// Normals stay unbent (the tilt is < 10 degrees where anything is still readable).
//
// Globals are pushed per camera by Drift.Visuals.CurvedWorld (RenderPipelineManager.beginCameraRendering):
// only the main / scene / explicitly registered cameras get a bend, every other camera (island preview,
// Tilda's portrait) renders flat. With the globals left at zero everything here is a no-op.
// Anything below DRIFT_CURVE_GUARD_Y is never bent or fogged: off-world rigs (Tilda's portrait stage at
// y = -3000) would otherwise be pushed away by their huge distance to the focus.
float4 _CurveFocus;      // xy = world xz with zero bend
float  _CurveInvRadius;  // 1 / R, 0 = flat
float4 _CurveFogColor;   // rgb = horizon haze (the sky colour at the limb), a = max haze on solid things
float4 _CurveFogParams;  // x = start, y = 1 / (end - start) of the planar distance to zw (the point under the camera)

// Adventure ring (Drift.Islands.RingWorld): x = 1 / ring radius (0 = off, the cozy globe above applies),
// y = band centre x, z = half width of the band (its rim), w = half circumference.
float4 _CurveRing;

#define DRIFT_CURVE_GUARD_Y -1000.0

// The inside of a closed ring: a point dz along the track from the focus lands at the angle dz / R on a circle whose
// axis hangs R above the focus, its height pointing at the axis, so the sea climbs into the sky ahead and behind and
// z and z + circumference are drawn at the same place. Whatever lies more than half a circumference away folds onto
// the antipode, so the sea mesh never overlaps itself. Heights stop at R / 2 so high things (clouds) never cross the
// axis.
// Across the band the world simply ends at the two rims: everything past the lip is folded onto the band's own edge
// plane, so the sea closes cleanly into the rim and nothing is drawn outside it. The rims themselves are invisible
// (Drift.Visuals.RingRims only lays a foam line on the water there); the player is held inside by RingWorld.
float3 DriftRingWS(float3 positionWS)
{
    positionWS.x = clamp(positionWS.x, _CurveRing.y - _CurveRing.z, _CurveRing.y + _CurveRing.z);
    float invR = _CurveRing.x;
    float R = 1.0 / invR;
    float dz = clamp(positionWS.z - _CurveFocus.y, -_CurveRing.w, _CurveRing.w);
    float y = min(positionWS.y, 0.5 * R);
    float t = dz * invR;
    float h = sin(0.5 * t);
    return float3(positionWS.x, y + (R - y) * (2.0 * h * h), _CurveFocus.y + (R - y) * sin(t));
}

float3 DriftCurveWS(float3 positionWS)
{
    if (_CurveRing.x > 0.0 && positionWS.y >= DRIFT_CURVE_GUARD_Y) return DriftRingWS(positionWS);
    if (_CurveInvRadius <= 0.0 || positionWS.y < DRIFT_CURVE_GUARD_Y) return positionWS;
    float2 rel = positionWS.xz - _CurveFocus.xy;
    float d = length(rel);
    // Half-angle form: sin t / t and 1 - cos t stay exact for tiny t. Past 3 rad everything parks near the
    // antipode, which is always behind the planet.
    float arc = max(d * _CurveInvRadius, 1e-5);
    float t = min(arc, 3.0);
    float s2, c2;
    sincos(t * 0.5, s2, c2);
    float versin = 2.0 * s2 * s2;
    float y = positionWS.y;
    positionWS.xz = _CurveFocus.xy + rel * (2.0 * s2 * c2 / arc * (1.0 + y * _CurveInvRadius));
    positionWS.y = y * (1.0 - versin) - versin / _CurveInvRadius;
    return positionWS;
}

float4 DriftCurveHClip(float3 positionWS)
{
    return TransformWorldToHClip(DriftCurveWS(positionWS));
}

// 0 near the camera, 1 at the limb (positionWS = the unbent world position).
float DriftFogAmount(float3 positionWS)
{
    float d = length(positionWS.xz - _CurveFogParams.zw);
    float f = saturate((d - _CurveFogParams.x) * _CurveFogParams.y);
    return f * f * step(DRIFT_CURVE_GUARD_Y, positionWS.y);
}

float3 DriftFog(float3 col, float3 positionWS)
{
    return lerp(col, _CurveFogColor.rgb, DriftFogAmount(positionWS) * _CurveFogColor.a);
}

#endif
