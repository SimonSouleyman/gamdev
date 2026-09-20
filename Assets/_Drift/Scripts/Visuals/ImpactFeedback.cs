using Drift.Islands;
using UnityEngine;

namespace Drift.Visuals
{
    [ExecuteAlways]
    [DefaultExecutionOrder(150)]
    public class ImpactFeedback : MonoBehaviour
    {
        public WaterFeedback water;
        public float ringLife = 2.5f;
        [Range(0f, 1f)] public float minRingStrength = 0.35f;
        [Range(0f, 1f)] public float ringStrength = 0.5f;
        [Range(0.05f, 1f)] public float slowMoScale = 0.35f;
        public float slowMoDuration = 0.22f;
        public bool slowMoEnabled = false;

        public struct Ring
        {
            public Vector2 pos;
            public float age;
            public float strength;
            public bool active;
        }

        readonly Ring[] _rings = new Ring[2];
        float _lastImpactIntensity;
        float _slowMoLeft;
        float _restoreScale = 1f;

        public bool SlowMoActive { get; private set; }
        public float SlowMoRemaining => SlowMoActive ? _slowMoLeft : 0f;
        public Ring GetRing(int i) => _rings[Mathf.Clamp(i, 0, 1)];
        public int ActiveRingCount => (_rings[0].active ? 1 : 0) + (_rings[1].active ? 1 : 0);

        void OnEnable()
        {
            Island.Impact += OnImpact;
            Island.Merged += OnMerged;
            ResolveWater();
        }

        void OnDisable()
        {
            Island.Impact -= OnImpact;
            Island.Merged -= OnMerged;
            EndSlowMo();
        }

        void Update()
        {
            Step(Application.isPlaying ? Time.unscaledDeltaTime : 0f);
        }

        void ResolveWater()
        {
            if (water == null) water = GetComponent<WaterFeedback>();
            if (water == null) water = FindAnyObjectByType<WaterFeedback>();
        }

        void OnImpact(float intensity)
        {
            _lastImpactIntensity = Mathf.Max(_lastImpactIntensity, intensity);
        }

        void OnMerged(Island host, Island guest, float energy)
        {
            if (host == null || guest == null) return;
            Vector2 contact = (host.PlanarPosition + guest.PlanarPosition) * 0.5f;
            float intensity = Mathf.Max(_lastImpactIntensity, Mathf.Clamp01(energy / 220f));
            _lastImpactIntensity = 0f;
            TriggerAt(contact, intensity);
        }

        public void TriggerAt(Vector2 pos, float intensity)
        {
            int slot = _rings[0].active ? (_rings[1].active ? (_rings[0].age >= _rings[1].age ? 0 : 1) : 1) : 0;
            _rings[slot] = new Ring
            {
                pos = pos,
                age = 0f,
                strength = Mathf.Lerp(minRingStrength, 1f, Mathf.Clamp01(intensity)) * ringStrength,
                active = true
            };
            Push();

            if (!slowMoEnabled || !Application.isPlaying || Time.timeScale <= 0f) return;
            if (!SlowMoActive)
            {
                _restoreScale = Time.timeScale;
                Time.timeScale = slowMoScale;
                SlowMoActive = true;
            }
            _slowMoLeft = slowMoDuration;
        }

        public void Step(float unscaledDt)
        {
            float scale = Application.isPlaying ? Time.timeScale : 1f;
            float dt = unscaledDt * scale;
            for (int i = 0; i < 2; i++)
            {
                if (!_rings[i].active) continue;
                _rings[i].age += dt;
                if (_rings[i].age >= ringLife) _rings[i].active = false;
            }

            if (SlowMoActive)
            {
                _slowMoLeft -= unscaledDt;
                if (_slowMoLeft <= 0f) EndSlowMo();
            }
            Push();
        }

        void EndSlowMo()
        {
            if (!SlowMoActive) return;
            SlowMoActive = false;
            // Only restore if nobody else (pause menu) changed the scale during the beat.
            if (Mathf.Abs(Time.timeScale - slowMoScale) < 1e-4f) Time.timeScale = _restoreScale;
        }

        void Push()
        {
            if (water == null) ResolveWater();
            if (water == null) return;
            for (int i = 0; i < 2; i++)
            {
                var r = _rings[i];
                water.SetRing(i, r.pos, r.active ? r.age : 0f, r.active ? r.strength : 0f);
            }
        }
    }
}
