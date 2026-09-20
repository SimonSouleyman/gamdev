using UnityEngine;

namespace Drift.Core
{
    public enum SeaKind
    {
        Dolphin, Whale, SeaTurtle, Jellyfish, Ray, FlyingFish, RestingGull, BaitBall, Seaweed,
        SailBoat, FishingBoat, TradingCog, RowBoat,
        Driftwood, Barrel, Buoy, Bottle, PalmLog,
        WhaleCalf, WhaleBull
    }

    public static class SeaNames
    {
        public static string German(SeaKind k)
        {
            switch (k)
            {
                case SeaKind.Dolphin: return "Delfin";
                case SeaKind.Whale: return "Wal";
                case SeaKind.SeaTurtle: return "Meeresschildkröte";
                case SeaKind.Jellyfish: return "Qualle";
                case SeaKind.Ray: return "Rochen";
                case SeaKind.FlyingFish: return "Fliegender Fisch";
                case SeaKind.RestingGull: return "Möwe";
                case SeaKind.BaitBall: return "Fischschwarm";
                case SeaKind.Seaweed: return "Seetang";
                case SeaKind.SailBoat: return "Segelboot";
                case SeaKind.FishingBoat: return "Fischerboot";
                case SeaKind.TradingCog: return "Handelsschiff";
                case SeaKind.RowBoat: return "Ruderboot";
                case SeaKind.Driftwood: return "Treibholz";
                case SeaKind.Barrel: return "Fass";
                case SeaKind.Buoy: return "Boje";
                case SeaKind.Bottle: return "Flaschenpost";
                case SeaKind.PalmLog: return "Palmenstamm";
                case SeaKind.WhaleCalf: return "Walkalb";
                case SeaKind.WhaleBull: return "Walbulle";
            }
            return k.ToString();
        }
    }

    // Optional component on an island GameObject (settlements): ShipSystem asks it before it lets a stranded hull
    // come to rest at a spot, so boats never end up inside buildings. local = island body space (Island.ToLocal).
    public interface IShoreBlocker
    {
        bool BlocksShore(Vector2 local, float radius);
    }

    // The pure parts of the sea systems (Drift.Visuals): torus maths, deterministic cell seeding, steering
    // around circles, range fades and the storm / night switches. No state, no allocation.
    public static class SeaMath
    {
        public const float SailsDownStorm = 0.35f;
        public const float SailsUpStorm = 0.2f;
        public const float LanternOnNight = 0.45f;
        public const float LanternOffNight = 0.35f;

        public static uint Hash(uint a, uint b)
        {
            uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u) * 0x85EBCA77u;
            h ^= h >> 15; h *= 0xC2B2AE3Du;
            h ^= h >> 13; h *= 0x27D4EB2Fu;
            h ^= h >> 16;
            return h;
        }

        public static float Rand(uint h, int k) => (Hash(h, (uint)k) & 0xFFFFFFu) / 16777216f;

        public static int Mod(int a, int n) => ((a % n) + n) % n;

        public static long Key(int cx, int cy) => ((long)cx << 32) | (uint)cy;

        public static int WrapCells(float worldPeriod, float cellSize) =>
            Mathf.Max(1, Mathf.RoundToInt(worldPeriod / Mathf.Max(0.1f, cellSize)));

        // Same value for every copy of a cell on the torus: the cell id is wrapped before hashing.
        public static uint CellSeed(int seed, int cx, int cy, int wrapCells) =>
            Hash(Hash((uint)seed, (uint)Mod(cx, wrapCells)), (uint)Mod(cy, wrapCells));

        public static Vector2 CellPoint(int cx, int cy, float cellSize, uint h) =>
            new Vector2((cx + 0.15f + 0.7f * Rand(h, 0)) * cellSize, (cy + 0.15f + 0.7f * Rand(h, 1)) * cellSize);

        public static float WrapDelta(float from, float to, float period)
        {
            float d = to - from;
            if (period <= 0f) return d;
            d -= Mathf.Round(d / period) * period;
            return d;
        }

        public static Vector2 WrapDelta(Vector2 from, Vector2 to, float period) =>
            new Vector2(WrapDelta(from.x, to.x, period), WrapDelta(from.y, to.y, period));

        public static float WrapDistance(Vector2 a, Vector2 b, float period) => WrapDelta(a, b, period).magnitude;

        // 1 inside `inner`, 0 at `outer`: things grow in / shrink out at the range edge instead of popping.
        public static float EdgeFade(float distance, float inner, float outer)
        {
            if (outer <= inner) return distance < outer ? 1f : 0f;
            float t = Mathf.Clamp01((outer - distance) / (outer - inner));
            return t * t * (3f - 2f * t);
        }

        public static bool SailsDown(float storm, bool wasDown) => storm > (wasDown ? SailsUpStorm : SailsDownStorm);

        public static bool LanternOn(float night, bool wasOn) => night > (wasOn ? LanternOffNight : LanternOnNight);

        public static int Capped(int wanted, int cap) => wanted < 0 ? 0 : wanted > cap ? cap : wanted;

        // Circles are (x, y, radius). Returns a unit heading: `desired` bent around every circle that lies within
        // lookAhead on the way; inside a circle's margin the heading turns outward so a mover that steps less than
        // `margin` per call never reaches the circle itself.
        public static Vector2 SteerAvoid(Vector2 pos, Vector2 desired, Vector3[] circles, int count, float lookAhead, float margin)
        {
            float dl = desired.magnitude;
            if (dl < 1e-5f) desired = Vector2.right; else desired /= dl;
            Vector2 steer = Vector2.zero;
            Vector2 escape = Vector2.zero;
            float escapeDepth = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 c = circles[i];
                Vector2 to = new Vector2(c.x - pos.x, c.y - pos.y);
                float dist = to.magnitude;
                float R = c.z + margin;
                if (dist < R)
                {
                    float depth = (R - dist) / Mathf.Max(margin, 1e-3f);
                    if (depth > escapeDepth)
                    {
                        escapeDepth = depth;
                        Vector2 outward = dist > 1e-4f ? -to / dist : -desired;
                        Vector2 tangent = new Vector2(-outward.y, outward.x);
                        if (Vector2.Dot(tangent, desired) < 0f) tangent = -tangent;
                        float k = Mathf.Clamp01(depth);
                        escape = outward * (0.35f + k) + tangent * (1f - k);
                    }
                    continue;
                }
                float along = Vector2.Dot(to, desired);
                if (along <= 0f || along > lookAhead + R) continue;
                float lateral = desired.x * to.y - desired.y * to.x;
                float al = Mathf.Abs(lateral);
                if (al >= R) continue;
                float urgency = 1f - Mathf.Clamp01((dist - R) / Mathf.Max(lookAhead, 1e-3f));
                Vector2 perp = lateral > 0f ? new Vector2(desired.y, -desired.x) : new Vector2(-desired.y, desired.x);
                steer += perp * (urgency * (1.3f - al / R));
            }
            if (escapeDepth > 0f) return escape.normalized;
            Vector2 r = desired + steer * 2.5f;
            return r.sqrMagnitude > 1e-6f ? r.normalized : new Vector2(-desired.y, desired.x);
        }

        // Pure visual shove: a point inside the circle is moved to its rim.
        public static Vector2 PushOut(Vector2 pos, Vector2 center, float radius)
        {
            Vector2 d = pos - center;
            float l = d.magnitude;
            if (l >= radius) return pos;
            if (l < 1e-4f) return center + Vector2.right * radius;
            return center + d * (radius / l);
        }

        // ---- boats running aground (ShipSystem.Beaching) ----

        public const int MaxBeachedPerIsland = 4;
        public const float BeachMinHeight = 0.02f;
        public const float BeachMaxHeight = 0.12f;
        public const float BeachRefloatHeight = -0.02f;
        public const float BeachBuriedHeight = 0.35f;
        // Rise per unit. The shores of this world are steep (0.6-1.8 at the waterline); rock stacks reach 3.
        public const float BeachSlopeLimit = 2f;
        public const float BeachPinnedSeconds = 1.5f;
        public const float BeachPinnedFactor = 0.35f;
        public const float BeachMinSeconds = 20f;
        public const float BeachMaxSeconds = 60f;
        public const float TiedIslandSpeed = 0.6f;
        public const float TiedMaxOverdue = 40f;

        // Closing speed (u/s, along the shore normal) above which an island that reaches a boat throws it on
        // the beach. Rowing boats strand easily, the cog is heavy and gets shoved aside first.
        public static float BeachClosingSpeed(SeaKind k)
        {
            switch (k)
            {
                case SeaKind.RowBoat: return 0.6f;
                case SeaKind.FishingBoat: return 1.3f;
                case SeaKind.SailBoat: return 1.7f;
                case SeaKind.TradingCog: return 3.4f;
            }
            return float.MaxValue;
        }

        // How fast a hull can be shoved / can scramble out of the way; always above BeachClosingSpeed, so a boat
        // that is not beached is never overrun either.
        public static float EvadeSpeed(SeaKind k)
        {
            switch (k)
            {
                case SeaKind.RowBoat: return 1.2f;
                case SeaKind.FishingBoat: return 2f;
                case SeaKind.SailBoat: return 2.4f;
                case SeaKind.TradingCog: return 4.2f;
            }
            return 3f;
        }

        // Speed at which the shore approaches the boat; `outward` is the unit shore normal pointing out to sea.
        public static float ClosingSpeed(Vector2 islandVel, Vector2 boatVel, Vector2 outward) =>
            Vector2.Dot(islandVel - boatVel, outward);

        // pinnedSeconds: how long the hull has been stuck over the shelf of a moving island (caught between the
        // shore and the travel direction); then a fraction of the threshold is enough.
        public static bool ShouldBeach(SeaKind kind, float closing, float pinnedSeconds, int beachedOnIsland, int cap)
        {
            if (beachedOnIsland >= Mathf.Min(cap, MaxBeachedPerIsland)) return false;
            float t = BeachClosingSpeed(kind);
            if (closing >= t) return true;
            return pinnedSeconds >= BeachPinnedSeconds && closing >= t * BeachPinnedFactor;
        }

        // Two islands squeeze a boat: it ends up on the one that closes faster (0 = a, 1 = b).
        public static int FasterHost(float closingA, float closingB) => closingB > closingA ? 1 : 0;

        public static bool ValidBeachSpot(float height, float slope) =>
            height >= BeachMinHeight && height <= BeachMaxHeight && slope <= BeachSlopeLimit;

        // Built over, uplifted by a merge or turned into a cliff: the hull has to move.
        public static bool BeachSpotLost(float height, float slope, bool blocked) =>
            blocked || height > BeachBuriedHeight || slope > BeachSlopeLimit * 1.5f;

        public static bool ShouldRefloat(float secondsLeft, float spotHeight, bool hostAlive) =>
            !hostAlive || secondsLeft <= 0f || spotHeight < BeachRefloatHeight;

        public static float BeachSeconds(uint seed) => Mathf.Lerp(BeachMinSeconds, BeachMaxSeconds, Rand(seed, 91));

        // Signed roll (radians) of a stranded hull: 18-35 degrees (the heavy cog 18-24), leaning downhill so its
        // bottom lies against the bank; a steep bank tips it further. downhillOnRight: dot(downhill, hull right),
        // its size is the slope across the hull.
        public static float BeachRoll(uint seed, float downhillOnRight, bool heavy)
        {
            float max = heavy ? 24f : 35f;
            float deg = Mathf.Lerp(18f, max, Rand(seed, 92));
            deg = Mathf.Clamp(Mathf.Max(deg, Mathf.Atan(Mathf.Abs(downhillOnRight)) * Mathf.Rad2Deg), 18f, max);
            return (downhillOnRight >= 0f ? -deg : deg) * Mathf.Deg2Rad;
        }

        // A boat tied to a dock stays tied while its island is under way (at most TiedMaxOverdue seconds past
        // its planned departure), then casts off.
        public static bool StaysTied(float islandSpeed, float overdueSeconds) =>
            islandSpeed > TiedIslandSpeed && overdueSeconds < TiedMaxOverdue;

        // Island body space, identical to Island.ToLocal / ToWorld: x = body right, y = body forward.
        public static Vector2 BodyRight(Vector2 bodyForward) => new Vector2(bodyForward.y, -bodyForward.x);

        public static Vector2 ToBodyLocal(Vector2 world, Vector2 islandPos, Vector2 bodyForward)
        {
            Vector2 d = world - islandPos;
            return new Vector2(Vector2.Dot(d, BodyRight(bodyForward)), Vector2.Dot(d, bodyForward));
        }

        public static Vector2 FromBodyLocal(Vector2 local, Vector2 islandPos, Vector2 bodyForward) =>
            islandPos + BodyRight(bodyForward) * local.x + bodyForward * local.y;

        public static Vector2 DirToBodyLocal(Vector2 worldDir, Vector2 bodyForward) =>
            new Vector2(Vector2.Dot(worldDir, BodyRight(bodyForward)), Vector2.Dot(worldDir, bodyForward));

        public static Vector2 DirFromBodyLocal(Vector2 localDir, Vector2 bodyForward) =>
            BodyRight(bodyForward) * localDir.x + bodyForward * localDir.y;

        // ---- whales ----

        public const int MaxWhalePodsInRange = 2;
        public const int MaxWhaleLonersInRange = 2;
        public const float WhaleClearance = 12f;
        public const float BreachMinDistance = 25f;
        public const float BreachMaxDistance = 80f;
        public const float BreachMaxStorm = 0.3f;

        public static bool WhaleSpawnAllowed(bool pod, int podsInRange, int lonersInRange, int podCap, int lonerCap) =>
            pod ? podsInRange < Mathf.Min(podCap, MaxWhalePodsInRange) : lonersInRange < Mathf.Min(lonerCap, MaxWhaleLonersInRange);

        // 2-4 animals; more than half of the pods bring a calf (always the last member, its mother is member 0).
        public static int WhalePodSize(uint h, out bool calf)
        {
            float r = Rand(h, 6);
            calf = Rand(h, 8) < 0.6f;
            return r < 0.4f ? 2 : r < 0.78f ? 3 : 4;
        }

        public static bool IsWhaleCalf(int member, int count, bool calf) => calf && count >= 2 && member == count - 1;

        // Loose formation in whale lengths (x right, y forward) around the leader / mother (member 0). Adults keep
        // about a body length apart, the calf swims tucked in at its mother's flank.
        public static Vector2 WhalePodOffset(int member, int count, bool calf, uint seed)
        {
            if (member <= 0) return Vector2.zero;
            float side = (seed & 1u) == 0u ? 1f : -1f;
            if (IsWhaleCalf(member, count, calf)) return new Vector2(-side * 0.4f, -0.14f);
            Vector2 jitter = new Vector2(Rand(seed, 30 + member) - 0.5f, Rand(seed, 40 + member) - 0.5f) * 0.24f;
            switch (member)
            {
                case 1: return new Vector2(side * 1.0f, -0.75f) + jitter;
                case 2: return new Vector2(-side * 1.15f, -1.3f) + jitter;
                default: return new Vector2(side * 0.25f, -2.1f) + jitter;
            }
        }

        // Seconds a member's breathing lags behind the leader, so the blows come one after the other.
        public static float WhaleBreathLag(int member, int count, bool calf) =>
            IsWhaleCalf(member, count, calf) ? 0.7f : member * 1.3f;

        public static bool BreachAllowed(float storm, float playerDistance, bool clearWater) =>
            clearWater && storm < BreachMaxStorm && playerDistance >= BreachMinDistance && playerDistance <= BreachMaxDistance;

        // Height of the water shader's main swell at a point (unit amplitude), so floating things heave in step.
        public static float Swell(Vector2 p, float t) =>
            Mathf.Sin((0.914f * p.x + 0.406f * p.y) * 0.63f + t * 0.63f) * 0.65f +
            Mathf.Sin((-0.6f * p.x + 0.8f * p.y) * 0.99f + t * 0.8f) * 0.35f;
    }
}
