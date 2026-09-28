#ifndef DRIFT_MOTION_INCLUDED
#define DRIFT_MOTION_INCLUDED

// Smooth motion for meshes the CPU bakes at 15 Hz or so (Drift.Core.CloseUpMotion, herds and far flocks):
// prev = (the vertex's object-space position at the previous bake, w). w = 1 + wrapped bake clock + 256 * duration
// code (1/60 s steps); w below 256 = no previous bake, the vertex stands where it is baked (a mesh without the
// channel reads 0, or (0,0,0,1) on GLES). The vertex slides from
// prev to its baked position over the duration after the bake, so a mover never jumps between two bakes.
float _DriftMotionClock;

float3 DriftMotion(float3 positionOS, float4 prev)
{
    if (prev.w < 256.0) return positionOS;
    float code = floor(prev.w / 256.0);
    float at = prev.w - code * 256.0 - 1.0;
    // The bake time comes back from w a fraction of a millisecond off: a tiny negative age is "just baked", only a
    // big one means the clock wrapped (slides last well under a second).
    float age = _DriftMotionClock - at;
    age += age < -1.0 ? 128.0 : 0.0;
    return lerp(prev.xyz, positionOS, saturate(age / max(code / 60.0, 1e-3)));
}

#endif
