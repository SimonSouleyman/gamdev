using System.Collections.Generic;
using UnityEngine;

namespace Drift.Tectonics
{
    public struct StormData
    {
        public Vector2 center;
        public float radius;
        public float age;
        public float lifetime;
        public float strength;
        public float gustPhase;
        // 0 = the wind blows one way through the whole storm (weather storms), 1 = it circles the eye, so driving
        // through one flank pushes the island one way and through the other flank the other way (ring storms).
        public float swirl;
        // Adventure: storms that lie on the ring as obstacles. They never blow over (lifetime = infinity) and are
        // placed, moved and removed by slot; slot < 0 on every weather storm.
        public int slot;
        public bool IsRing => slot >= 0;
        public float Envelope(float fade)
        {
            float f = Mathf.Max(0.01f, fade);
            float rise = Mathf.Clamp01(age / f);
            float fall = Mathf.Clamp01((lifetime - age) / f);
            float t = Mathf.Min(rise, fall);
            return t * t * (3f - 2f * t);
        }
    }

    // Where the storms of the adventure ring lie and how many of them there are. Pure arithmetic so the placement
    // rule can be tested without a scene: every storm keeps a lane of clear water to one rim open, so the track is
    // never walled off and the player can plan a line around it from far away.
    public static class RingStormPlan
    {
        // Storms on the track at a given RingWorld.Level (1, 2, 3 ...). Fractional rates are allowed: 0.5 per level
        // means one more storm every second level.
        public static int CountForLevel(int level, float atFirstLevel, float perLevel, int max)
        {
            float n = Mathf.Max(0f, atFirstLevel) + Mathf.Max(0f, perLevel) * Mathf.Max(0, level - 1);
            return Mathf.Clamp(Mathf.FloorToInt(n + 1e-4f), 0, Mathf.Max(0, max));
        }

        // Slot `index` of `slots`: one stratum of the ring each (so adding a storm never moves the others), jittered
        // along the track, hugging one rim, and never wider than the band minus `freeLane`.
        public static void Place(int index, int slots, float circumference, float centerX, float halfWidth,
                                 float wantRadius, float freeLane, int seed,
                                 out float along, out float x, out float radius)
        {
            slots = Mathf.Max(1, slots);
            circumference = Mathf.Max(1f, circumference);
            float cell = circumference / slots;
            float j = Hash01(seed, index * 3 + 1);
            along = Mathf.Repeat((index + 0.5f + 0.6f * (j - 0.5f)) * cell, circumference);

            float side = Hash01(seed, index * 3 + 2) < 0.5f ? -1f : 1f;
            // Alternate sides down the ring: two storms in a row hugging the same rim would make one long wall.
            if ((index & 1) == 1) side = -side;
            float offset = Mathf.Lerp(0.25f, 0.65f, Hash01(seed, index * 3 + 3)) * Mathf.Max(0f, halfWidth) * side;
            x = centerX + offset;
            // Clear water from the storm's far edge to the opposite rim: halfWidth + |offset| - radius.
            float room = Mathf.Max(0f, halfWidth) + Mathf.Abs(offset) - Mathf.Max(0f, freeLane);
            radius = Mathf.Clamp(wantRadius, 1f, Mathf.Max(1f, room));
        }

        // The clear water left beside a storm placed at `x` with `radius`, on the side away from it.
        public static float Lane(float x, float radius, float centerX, float halfWidth) =>
            halfWidth + Mathf.Abs(x - centerX) - radius;

        static float Hash01(int seed, int n)
        {
            unchecked
            {
                uint h = (uint)(seed * 73856093) ^ (uint)(n * 19349663) ^ 0x9E3779B9u;
                h ^= h << 13; h ^= h >> 17; h ^= h << 5;
                return (h & 0xFFFFFFu) / (float)0x1000000;
            }
        }
    }

    // Storms start on transform-boundary Slip events and sit over the sea for a while. Everything an
    // island, the camera or audio needs is positional: IntensityAt / GustAt. Storms are transient and
    // not saved.
    [ExecuteAlways]
    public class StormSystem : MonoBehaviour
    {
        public static StormSystem Instance { get; private set; }

        public float radius = 35f;
        public float lifetime = 25f;
        public float fadeTime = 4f;
        public int maxActive = 3;
        public float cooldown = 90f;
        public float gustStrength = 2.5f;
        public float gustRotation = 0.35f;
        public float plateauFraction = 0.55f;
        public float spawnRange = 160f;
        public bool drawGizmos = true;

        // Adventure: while the ring lays its own storms on the track, plate events must not add weather storms on
        // top of them (Drift.Visuals.RingStorms sets and clears this).
        [System.NonSerialized] public bool suppressEventStorms;

        readonly List<StormData> _storms = new();
        PlateSystem _subscribed;
        float _cooldownLeft;
        float _clock;
        uint _rng = 2463534242u;

        public IReadOnlyList<StormData> Storms => _storms;
        public int ActiveCount => _storms.Count;
        public float Clock => _clock;
        public float CooldownLeft => _cooldownLeft;

        public int RingCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _storms.Count; i++) if (_storms[i].IsRing) n++;
                return n;
            }
        }

        public int WeatherCount => _storms.Count - RingCount;

        void OnEnable()
        {
            Instance = this;
            _storms.Clear();
            _cooldownLeft = 0f;
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            Subscribe();
            if (!Application.isPlaying) return;
            Step(Time.deltaTime);
        }

        void Subscribe()
        {
            var ps = PlateSystem.Instance;
            if (ps == _subscribed) return;
            Unsubscribe();
            _subscribed = ps;
            if (_subscribed != null) _subscribed.EventStarted += OnPlateEvent;
        }

        void Unsubscribe()
        {
            if (_subscribed != null) _subscribed.EventStarted -= OnPlateEvent;
            _subscribed = null;
        }

        float NextRandom()
        {
            uint x = _rng;
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            _rng = x;
            return (x & 0xFFFFFFu) / (float)0x1000000;
        }

        void OnPlateEvent(PlateEventData e)
        {
            if (e.type != PlateEventType.Slip || suppressEventStorms) return;
            if (_cooldownLeft > 0f || WeatherCount >= maxActive) return;
            var ps = PlateSystem.Instance;
            if (ps != null && ps.focus != null && spawnRange > 0f)
            {
                Vector2 f = new Vector2(ps.focus.position.x, ps.focus.position.z);
                if (Vector2.Distance(f, e.position) > spawnRange) return;
            }
            StartStorm(e.position, e.strength);
        }

        public StormData StartStorm(Vector2 center, float strength, bool ignoreLimits = false)
        {
            if (!ignoreLimits && WeatherCount >= maxActive) RemoveFirstWeather();
            var s = new StormData
            {
                center = center,
                radius = radius,
                age = 0f,
                lifetime = lifetime,
                strength = Mathf.Clamp01(0.6f + 0.4f * strength),
                gustPhase = NextRandom() * Mathf.PI * 2f,
                slot = -1,
            };
            _storms.Add(s);
            _cooldownLeft = cooldown;
            return s;
        }

        void RemoveFirstWeather()
        {
            for (int i = 0; i < _storms.Count; i++)
                if (!_storms[i].IsRing) { _storms.RemoveAt(i); return; }
        }

        // ------------------------------------------------------------ ring storms (Adventure)

        // A storm that lies on the adventure track for good: it never blows over, so a storm dodged once comes round
        // again with the ring. One per slot; Drift.Visuals.RingStorms owns the slots and keeps the centre wrapped to
        // the part of the ring that is drawn.
        public StormData StartRingStorm(Vector2 center, float radius, float strength, float swirl, int slot)
        {
            RemoveRingStorm(slot);
            var s = new StormData
            {
                center = center,
                radius = Mathf.Max(1f, radius),
                age = 0f,
                lifetime = float.PositiveInfinity,
                strength = Mathf.Clamp01(strength),
                gustPhase = NextRandom() * Mathf.PI * 2f,
                swirl = Mathf.Clamp01(swirl),
                slot = Mathf.Max(0, slot),
            };
            _storms.Add(s);
            return s;
        }

        public bool HasRingSlot(int slot)
        {
            for (int i = 0; i < _storms.Count; i++) if (_storms[i].slot == slot && slot >= 0) return true;
            return false;
        }

        public bool RemoveRingStorm(int slot)
        {
            if (slot < 0) return false;
            for (int i = 0; i < _storms.Count; i++)
                if (_storms[i].slot == slot) { _storms.RemoveAt(i); return true; }
            return false;
        }

        // The ring's z coordinate runs on without wrapping, the drawn band does not: the centre is moved by whole
        // circumferences so the storm stays in the half of the ring the bend draws.
        public void SetRingStormCenter(int slot, Vector2 center)
        {
            if (slot < 0) return;
            for (int i = 0; i < _storms.Count; i++)
                if (_storms[i].slot == slot)
                {
                    var s = _storms[i];
                    s.center = center;
                    _storms[i] = s;
                    return;
                }
        }

        public void ClearRingStorms()
        {
            for (int i = _storms.Count - 1; i >= 0; i--) if (_storms[i].IsRing) _storms.RemoveAt(i);
        }

        public void ClearStorms()
        {
            _storms.Clear();
            _cooldownLeft = 0f;
        }

        public void Step(float dt)
        {
            _clock += dt;
            _cooldownLeft = Mathf.Max(0f, _cooldownLeft - dt);
            for (int i = _storms.Count - 1; i >= 0; i--)
            {
                var s = _storms[i];
                s.age += dt;
                if (s.age >= s.lifetime) _storms.RemoveAt(i);
                else _storms[i] = s;
            }
        }

        float Falloff(in StormData s, float d)
        {
            float inner = s.radius * plateauFraction;
            if (d <= inner) return 1f;
            if (d >= s.radius) return 0f;
            float t = 1f - (d - inner) / (s.radius - inner);
            return t * t * (3f - 2f * t);
        }

        public float IntensityAt(Vector2 pos)
        {
            float best = 0f;
            for (int i = 0; i < _storms.Count; i++)
            {
                var s = _storms[i];
                float d = Vector2.Distance(pos, s.center);
                if (d >= s.radius) continue;
                best = Mathf.Max(best, s.Envelope(fadeTime) * Falloff(s, d));
            }
            return best;
        }

        public Vector2 GustAt(Vector2 pos, float time)
        {
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < _storms.Count; i++)
            {
                var s = _storms[i];
                Vector2 rel = pos - s.center;
                float d = rel.magnitude;
                if (d >= s.radius) continue;
                float k = s.Envelope(fadeTime) * Falloff(s, d) * s.strength * gustStrength;
                float a = s.gustPhase + time * gustRotation + Mathf.Sin(time * 0.9f + s.gustPhase) * 0.35f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                sum += SwirlDirection(in s, rel, d, dir) * k;
            }
            return sum;
        }

        // The wind inside a storm: straight through it at swirl 0, circling the eye at swirl 1. The circle fades out
        // towards the very centre, where a tangent is undefined and the eye is calm anyway.
        public static Vector2 SwirlDirection(in StormData s, Vector2 rel, float d, Vector2 straight)
        {
            if (s.swirl <= 0f) return straight;
            float w = Mathf.Clamp01(s.swirl) * Mathf.Clamp01(d / Mathf.Max(1e-3f, s.radius * 0.25f));
            if (w <= 0f || d <= 1e-4f) return straight;
            float spin = Mathf.Repeat(s.gustPhase, Mathf.PI * 2f) < Mathf.PI ? 1f : -1f;
            Vector2 tangent = new Vector2(-rel.y, rel.x) * (spin / d);
            Vector2 mix = Vector2.Lerp(straight, tangent, w);
            return mix.sqrMagnitude > 1e-6f ? mix.normalized : straight;
        }

        public bool NearestStorm(Vector2 pos, out StormData storm)
        {
            storm = default;
            float best = float.MaxValue;
            for (int i = 0; i < _storms.Count; i++)
            {
                float d = Vector2.Distance(pos, _storms[i].center);
                if (d < best) { best = d; storm = _storms[i]; }
            }
            return best < float.MaxValue;
        }

        void OnDrawGizmos()
        {
            if (!drawGizmos) return;
            foreach (var s in _storms)
            {
                float e = s.Envelope(fadeTime);
                Gizmos.color = new Color(0.35f, 0.4f, 0.6f, 0.25f + 0.5f * e);
                Vector3 c = new Vector3(s.center.x, 0.5f, s.center.y);
                DrawCircle(c, s.radius);
                DrawCircle(c, s.radius * plateauFraction);
            }
        }

        static void DrawCircle(Vector3 c, float r)
        {
            const int n = 40;
            Vector3 prev = c + new Vector3(r, 0f, 0f);
            for (int i = 1; i <= n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                Vector3 p = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }
}
