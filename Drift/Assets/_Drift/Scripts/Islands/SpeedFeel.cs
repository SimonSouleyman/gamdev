using UnityEngine;

namespace Drift.Islands
{
    // The curves behind "driving feels fast": how much the island is really under way (Drive), how much of that
    // is the water's doing (Flow, built up while travelling with the plate current) and whether it is riding a
    // plate boundary (Surf). Camera, water and audio each keep their own Tracker and step it from the same
    // island state, so they agree without depending on each other's execution order.
    public static class SpeedFeel
    {
        // Below this share of the island's own top speed nothing reacts: puttering around stays calm.
        public const float DriveStart = 0.35f;
        // Beyond its own top speed (current + surf carry it) this much more counts as the full bonus.
        public const float OverdriveSpan = 0.5f;
        public const float OverdriveWeight = 0.4f;
        // Drive at the island's own top speed is 1; the current can push it to this.
        public const float Max = 1f + OverdriveWeight;

        public static float Drive(float speed, float maxSpeed)
        {
            if (maxSpeed <= 1e-3f) return 0f;
            float raw = speed / maxSpeed;
            float own = Mathf.Clamp01((raw - DriveStart) / (1f - DriveStart));
            own = own * own * (3f - 2f * own);
            float over = Mathf.Clamp01((raw - 1f) / OverdriveSpan);
            return own + OverdriveWeight * over;
        }

        // -1 = the water pushes straight against the way the island drives, +1 = exactly with it.
        public static float CurrentAlignment(Vector2 carry, Vector2 self)
        {
            float c = carry.magnitude;
            if (c < 1e-3f || self.sqrMagnitude < 1e-4f) return 0f;
            return Mathf.Clamp(Vector2.Dot(carry / c, self.normalized), -1f, 1f);
        }

        // Where the flow build-up is heading: only travelling WITH a current that actually carries counts.
        public const float FlowFullCurrent = 2.5f;

        public static float FlowTarget(float alignment, float carrySpeed)
        {
            float with = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.9f, alignment));
            return with * Mathf.Clamp01(carrySpeed / FlowFullCurrent);
        }

        // Exponential approach with separate rise and fall times: flow takes seconds to build and drops away
        // quickly when the island leaves the stream, so losing it is as readable as finding it.
        public static float Approach(float value, float target, float dt, float riseSeconds, float fallSeconds)
        {
            if (dt <= 0f) return target;
            float tau = target > value ? riseSeconds : fallSeconds;
            return Mathf.Lerp(value, target, 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, tau)));
        }

        // Extra distance/field of view share, capped so an overdriven island does not fly off the screen.
        public static float Framing(float drive) => Mathf.Clamp(drive, 0f, Max);

        // 0 until `start`, then quadratic to 1 at the top: the shake only ever shows up at real speed.
        public static float ShakeAmount(float drive, float start)
        {
            float span = Mathf.Max(0.05f, Max - start);
            float x = Mathf.Clamp01((drive - start) / span);
            return x * x;
        }

        // Lean into the turn, in degrees: full only when the island is really moving.
        public static float Bank(float turnRateDegPerSec, float topTurnRate, float drive, float maxDegrees)
        {
            if (maxDegrees <= 0f || topTurnRate <= 1e-3f) return 0f;
            float t = Mathf.Clamp(turnRateDegPerSec / topTurnRate, -1f, 1f);
            return t * maxDegrees * Mathf.Clamp01(drive);
        }

        // True on the frame the island jumps onto a plate boundary: a kick is due only when the surf has just
        // crossed the threshold, the cooldown is over and no other boost is running.
        public static bool CatchesSurf(float surf, bool wasSurfing, float threshold, float cooldownLeft, bool boosting) =>
            surf >= threshold && !wasSurfing && cooldownLeft <= 0f && !boosting;

        // The gentle push for staying with the current, as a top-speed factor (1 = none).
        public static float FlowPush(float flow, float threshold, float factor)
        {
            if (factor <= 1f || flow < threshold) return 1f;
            return 1f + (factor - 1f) * Mathf.InverseLerp(threshold, 1f, flow);
        }

        // How much of a full boost is running (0..1): Island.BoostFactor against the factor that counts as full.
        public static float BoostShare(float boostFactor, float fullFactor)
        {
            if (fullFactor <= 1.001f) return boostFactor > 1.001f ? 1f : 0f;
            return Mathf.Clamp01((boostFactor - 1f) / (fullFactor - 1f));
        }

        // How hard a freshly collected boost punches: by how much it renewed the boost, never less than `min` so a
        // pickup while already at full boost is still felt a little.
        public static float PickupStrength(float factorBefore, float factorAfter, float fullFactor, float min)
        {
            float gain = BoostShare(factorAfter, fullFactor) - BoostShare(factorBefore, fullFactor);
            return Mathf.Clamp01(Mathf.Max(min, gain));
        }

        // A kick decaying from 1: k * exp(-dt / seconds). Stands still at dt <= 0 (pause).
        public static float Decay(float kick, float dt, float seconds)
        {
            if (dt <= 0f || kick <= 0f) return Mathf.Max(0f, kick);
            float k = kick * Mathf.Exp(-dt / Mathf.Max(0.01f, seconds));
            return k < 0.001f ? 0f : k;
        }

        // The visible shape of a decaying kick: 0 with zero slope when it starts, 1 halfway down, 0 again at the
        // end - a swell instead of a jump, so FOV punches never snap.
        public static float Bump(float kick)
        {
            float k = Mathf.Clamp01(kick);
            float b = 4f * k * (1f - k);
            return b * b;
        }

        // Adventure: the camera moves closer (and lower) with the speed instead of backing off - together with the
        // wider field of view the water streams past faster while the island keeps its size on screen.
        public static float RingDolly(float framing, float amount) =>
            1f - Mathf.Clamp01(amount) * Mathf.Clamp01(framing / Max);

        // The island radius the chase framing scales with: unchanged up to `knee`, above it only radius^soft, so a
        // continent is not framed from so far away that its animals shrink to a few pixels.
        public static float SoftRadius(float radius, float knee, float soft)
        {
            if (knee <= 0f || radius <= knee) return radius;
            return knee * Mathf.Pow(radius / knee, Mathf.Clamp01(soft));
        }

        // How far the idle "life zoom" closes in on an island of this radius (1 = not at all): nothing on small
        // islands, the full closeIn from twice minRadius on.
        public static float IdleCloseIn(float radius, float closeIn, float minRadius)
        {
            float t = Mathf.InverseLerp(minRadius, 2f * Mathf.Max(0.01f, minRadius), radius);
            return Mathf.Lerp(1f, Mathf.Clamp(closeIn, 0.1f, 1f), t);
        }

        // The surf tone's pitch (Hz): a clear rise while the boundary carries, so "I am surfing" is audible.
        public static float SurfHz(float surf, float baseHz = 165f, float span = 250f)
        {
            float s = Mathf.Clamp01(surf);
            return baseHz + span * s * s;
        }

        public sealed class Tracker
        {
            public float DriveResponse = 3.5f;
            public float FlowRiseSeconds = 2.5f;
            public float FlowFallSeconds = 0.5f;
            public float SurfRiseSeconds = 0.2f;
            public float SurfFallSeconds = 0.45f;

            public float Drive { get; private set; }
            public float Flow { get; private set; }
            public float Surf { get; private set; }
            public float Alignment { get; private set; }
            public float Speed { get; private set; }

            public void Reset()
            {
                Drive = Flow = Surf = Alignment = Speed = 0f;
            }

            public void Step(float speed, float maxSpeed, Vector2 carry, Vector2 self, float surfStrength, float dt)
            {
                Speed = speed;
                float target = SpeedFeel.Drive(speed, maxSpeed);
                Drive = dt > 0f ? Mathf.Lerp(Drive, target, 1f - Mathf.Exp(-DriveResponse * dt)) : target;
                Alignment = SpeedFeel.CurrentAlignment(carry, self);
                Flow = SpeedFeel.Approach(Flow, SpeedFeel.FlowTarget(Alignment, carry.magnitude), dt, FlowRiseSeconds, FlowFallSeconds);
                Surf = SpeedFeel.Approach(Surf, Mathf.Clamp01(surfStrength), dt, SurfRiseSeconds, SurfFallSeconds);
            }

            public void Step(Island island, float dt)
            {
                if (island == null || !island.isActiveAndEnabled || island.IsSunk)
                {
                    Step(0f, 1f, Vector2.zero, Vector2.zero, 0f, dt);
                    return;
                }
                // PlanarVelocity - WaterVelocity is exactly the plate current the island has taken on (_carry).
                Vector2 carry = island.PlanarVelocity - island.WaterVelocity;
                Step(island.PlanarVelocity.magnitude, island.MaxSpeed, carry, island.SelfVelocity, island.SurfStrength, dt);
            }
        }
    }
}
