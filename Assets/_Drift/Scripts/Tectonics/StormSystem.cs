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
        public float Envelope(float fade)
        {
            float f = Mathf.Max(0.01f, fade);
            float rise = Mathf.Clamp01(age / f);
            float fall = Mathf.Clamp01((lifetime - age) / f);
            float t = Mathf.Min(rise, fall);
            return t * t * (3f - 2f * t);
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

        readonly List<StormData> _storms = new();
        PlateSystem _subscribed;
        float _cooldownLeft;
        float _clock;
        uint _rng = 2463534242u;

        public IReadOnlyList<StormData> Storms => _storms;
        public int ActiveCount => _storms.Count;
        public float Clock => _clock;
        public float CooldownLeft => _cooldownLeft;

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
            if (e.type != PlateEventType.Slip) return;
            if (_cooldownLeft > 0f || _storms.Count >= maxActive) return;
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
            if (!ignoreLimits && _storms.Count >= maxActive) _storms.RemoveAt(0);
            var s = new StormData
            {
                center = center,
                radius = radius,
                age = 0f,
                lifetime = lifetime,
                strength = Mathf.Clamp01(0.6f + 0.4f * strength),
                gustPhase = NextRandom() * Mathf.PI * 2f
            };
            _storms.Add(s);
            _cooldownLeft = cooldown;
            return s;
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
                float d = Vector2.Distance(pos, s.center);
                if (d >= s.radius) continue;
                float k = s.Envelope(fadeTime) * Falloff(s, d) * s.strength * gustStrength;
                float a = s.gustPhase + time * gustRotation + Mathf.Sin(time * 0.9f + s.gustPhase) * 0.35f;
                sum += new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * k;
            }
            return sum;
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
