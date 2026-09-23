using UnityEngine;

namespace Drift.Core
{
    // What SeaLifeSystem.TryShowNear can put on show, in the order a roll rotates through.
    public enum SeaShowKind { Seal, DolphinPass, FlyingFish, Turtle, RayLeap, WhaleBlow, FishJump }

    // The pure parts of the cozy "something to watch every few seconds" sea (Drift.Visuals.SeaLifeSystem): picking a
    // show, where the camera meets the water, finding a coast along a sampled height profile and the motion curves
    // of seals, surfacing turtles, skipping flying fish and leaping rays. No state, no allocation.
    public static class SeaShow
    {
        public const int KindCount = 7;

        // The i-th kind to try for a roll: a rotation, so every roll starts somewhere else and still reaches all.
        public static SeaShowKind Order(int roll, int i) => (SeaShowKind)SeaMath.Mod(SeaMath.Mod(roll, KindCount) + i, KindCount);

        public static bool Has(int mask, SeaShowKind k) => (mask & (1 << (int)k)) != 0;

        // How big (u) a show is on screen: Moments.SubjectSize of what it reports. A far camera asks only for shows
        // at least as big as it needs (TryShowNear's minSize), so a seal too small to see does not use up the turn.
        public static float SubjectSize(SeaShowKind k)
        {
            switch (k)
            {
                case SeaShowKind.Seal: return Moments.SubjectSize(MomentKind.SealPopUp);
                case SeaShowKind.DolphinPass: return Moments.SubjectSize(MomentKind.DolphinJump);
                case SeaShowKind.FlyingFish: return Moments.SubjectSize(MomentKind.FlyingFish);
                case SeaShowKind.Turtle: return Moments.SubjectSize(MomentKind.TurtleBreath);
                case SeaShowKind.RayLeap: return Moments.SubjectSize(MomentKind.RayLeap);
                case SeaShowKind.WhaleBlow: return Moments.SubjectSize(MomentKind.WhaleSurface);
                default: return Moments.SubjectSize(MomentKind.FishJump);
            }
        }

        // First kind of the roll's rotation the mask allows, -1 when it allows none.
        public static int Pick(int roll, int mask)
        {
            for (int i = 0; i < KindCount; i++)
            {
                var k = Order(roll, i);
                if (Has(mask, k)) return (int)k;
            }
            return -1;
        }

        // Kinds whose cooldown has run out.
        public static int ReadyMask(float[] cooldowns)
        {
            int m = 0;
            if (cooldowns == null) return 0;
            for (int i = 0; i < KindCount && i < cooldowns.Length; i++)
                if (cooldowns[i] <= 0f) m |= 1 << i;
            return m;
        }

        // Where a camera ray meets the sea (y = 0) as a planar point. A ray that does not come down within
        // maxDistance (planar) ends at maxDistance along its planar direction instead.
        public static Vector2 WaterHit(Vector3 origin, Vector3 dir, float maxDistance)
        {
            Vector2 o = new Vector2(origin.x, origin.z);
            if (dir.y < -1e-4f && origin.y > 0f)
            {
                float t = -origin.y / dir.y;
                Vector2 hit = new Vector2(origin.x + dir.x * t, origin.z + dir.z * t);
                if ((hit - o).sqrMagnitude <= maxDistance * maxDistance) return hit;
            }
            Vector2 flat = new Vector2(dir.x, dir.z);
            float fl = flat.magnitude;
            return o + (fl > 1e-5f ? flat / fl : Vector2.up) * maxDistance;
        }

        // Half the width of the picture at a distance from the camera (vertical field of view, width / height).
        public static float HalfWidthAt(float distance, float verticalFovDeg, float aspect) =>
            Mathf.Max(0f, distance) * Mathf.Tan(Mathf.Clamp(verticalFovDeg, 1f, 170f) * 0.5f * Mathf.Deg2Rad) * Mathf.Max(0.05f, aspect);

        // A direction within `spread` radians either side of `forward`; u 0..1 runs across the arc.
        public static Vector2 ArcDirection(Vector2 forward, float spread, float u)
        {
            float l = forward.magnitude;
            Vector2 f = l > 1e-6f ? forward / l : Vector2.up;
            float a = (Mathf.Clamp01(u) * 2f - 1f) * spread;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(f.x * c - f.y * s, f.x * s + f.y * c);
        }

        // Evenly spread over the disc (sqrt of the radius roll), keyed on (h, k) and (h, k + 1).
        public static Vector2 DiscPoint(Vector2 center, float radius, uint h, int k)
        {
            float a = SeaMath.Rand(h, k) * Mathf.PI * 2f;
            float r = Mathf.Max(0f, radius) * Mathf.Sqrt(SeaMath.Rand(h, k + 1));
            return center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        // Heights sampled outward from inside an island (heights[0] innermost): `beach` is the last land sample
        // before the shore when it lies in beachMin..beachMax (a flat strip a seal can lie on, else -1), `water` the
        // first sample after the land that is deeper than waterDepth. False when the profile never goes from land
        // to such water.
        public static bool FindCoast(float[] heights, int count, float beachMin, float beachMax, float waterDepth, out int beach, out int water)
        {
            beach = -1;
            water = -1;
            bool land = false;
            if (heights == null) return false;
            count = Mathf.Min(count, heights.Length);
            for (int i = 0; i < count; i++)
            {
                float h = heights[i];
                if (h >= beachMin)
                {
                    land = true;
                    beach = h <= beachMax ? i : -1;
                    continue;
                }
                if (land && h < waterDepth)
                {
                    water = i;
                    return true;
                }
            }
            beach = -1;
            return false;
        }

        // One step of `step` units from `from` towards `to` round the origin (an island's centre in its body space):
        // along the arc and the radius at once, so a swimmer follows the coast instead of cutting across the land.
        public static Vector2 PolarStep(Vector2 from, Vector2 to, float step)
        {
            float ra = from.magnitude, rb = to.magnitude;
            if (ra < 1e-4f || rb < 1e-4f) return Vector2.MoveTowards(from, to, step);
            float aa = Mathf.Atan2(from.y, from.x);
            float da = Mathf.DeltaAngle(aa * Mathf.Rad2Deg, Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            float arc = Mathf.Abs(da) * 0.5f * (ra + rb);
            float dr = rb - ra;
            float total = Mathf.Sqrt(arc * arc + dr * dr);
            if (total <= step || total < 1e-5f) return to;
            float k = Mathf.Max(0f, step) / total;
            float a = aa + da * k, r = ra + dr * k;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        // ---- turtles ----

        public const float TurtleSurfaceSin = 5f / 6f;

        // 0 under water, rising to 1 while the turtle is up for a breath; one breath per 2 pi / rate seconds.
        public static float TurtleBreath(float clock, float phase, float rate) =>
            Mathf.Clamp01(Mathf.Sin(clock * rate + phase) * 6f - 5f);

        // The phase that makes a turtle breathing at `rate` start surfacing `lead` seconds after `clock`.
        public static float TurtleSurfacePhase(float clock, float rate, float lead) =>
            Mathf.Asin(TurtleSurfaceSin) - (clock + lead) * rate;

        // A surfacing is reported once, when the breath crosses this.
        public static bool Surfaced(float before, float now) => before < 0.35f && now >= 0.35f;

        // ---- seals ----

        // 0..1 how far a seal's head is out of the water during a look-around of `duration` seconds.
        public static float SealRise(float t, float duration)
        {
            if (t <= 0f || t >= duration) return 0f;
            float k = Mathf.Min(Mathf.Clamp01(t / 0.45f), Mathf.Clamp01((duration - t) / 0.5f));
            return k * k * (3f - 2f * k);
        }

        // Where the head looks (radians off the body heading) while the seal looks around.
        public static float SealLook(float t, float phase) =>
            Mathf.Sin(t * 1.3f + phase) * 0.8f + Mathf.Sin(t * 3.1f + phase * 2f) * 0.15f;

        // ---- flying fish ----

        // Height of a flying-fish glide skipping `hops` times over t 0..1: equal arcs, each 70 % as high as the last.
        // hopT is the time within the current arc (0..1).
        public static float SkipHeight(float t, int hops, float height, out float hopT)
        {
            hops = Mathf.Max(1, hops);
            if (t <= 0f) { hopT = 0f; return 0f; }
            if (t >= 1f) { hopT = 1f; return 0f; }
            float x = t * hops;
            int k = Mathf.Min(hops - 1, (int)x);
            hopT = x - k;
            return Mathf.Sin(hopT * Mathf.PI) * height * Mathf.Pow(0.7f, k);
        }

        public static int HopIndex(float t, int hops) => t <= 0f ? 0 : Mathf.Min(Mathf.Max(1, hops) - 1, (int)(t * Mathf.Max(1, hops)));

        // ---- rays ----

        // A ray's leap over t 0..1: height of the body (it starts and ends below the surface), a nose-up launch
        // that flattens into a belly flop, and a lazy roll.
        public static float RayLeap(float t, float height, out float pitch, out float roll)
        {
            t = Mathf.Clamp01(t);
            float s = t * t * (3f - 2f * t);
            pitch = Mathf.Lerp(0.95f, -0.12f, s);
            roll = Mathf.Sin(t * Mathf.PI * 2f) * 0.3f;
            return -0.4f + (Mathf.Max(0f, height) + 0.4f) * Mathf.Sin(t * Mathf.PI);
        }
    }
}
